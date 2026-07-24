import sys
import os

# Định nghĩa DummyWriter để hấp thụ toàn bộ dữ liệu in ra nếu luồng bị lỗi hoặc chạy ngầm (no console)
class DummyWriter:
    def write(self, x): pass
    def flush(self): pass

# Kiểm tra xem stdout/stderr có hợp lệ và ghi được không. 
# Nếu chạy ngầm từ WPF (không có console), fileno() hoặc các lệnh ghi sẽ lỗi, ta redirect ngay lập tức.
is_gui_background = False
try:
    if sys.stdout is None or not sys.stdout.writable():
        is_gui_background = True
    else:
        # Thử lấy file descriptor, nếu lỗi thì đây là luồng ảo chạy ngầm
        fd = sys.stdout.fileno()
except Exception:
    is_gui_background = True

if is_gui_background:
    sys.stdout = DummyWriter()
    sys.stderr = DummyWriter()
    # Khóa hàm print gốc của python để chắc chắn không gọi trực tiếp ra stdout
    import builtins
    builtins.print = lambda *args, **kwargs: None

from flask import Flask, request, jsonify, send_file
from paddleocr import PaddleOCR

import tempfile
import cv2
import numpy as np
import json
import io


print(__file__)
print(os.getcwd())

from converter import convert_page
from bubble_group import group_bubbles
from reading_order import sort_bubbles
from response_builder import build_response

app = Flask(__name__)

ocr_instances = {}

def get_ocr_instance(lang):
    if lang not in ocr_instances:
        print(f"Initializing PaddleOCR for language: {lang}")
        ocr_instances[lang] = PaddleOCR(
            lang=lang,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False
        )
    return ocr_instances[lang]


@app.route("/ocr", methods=["POST"])
def recognize():
    print(">>> RECOGNIZE CALLED <<<")
    try:
        if "image" not in request.files:
            return jsonify({
                "success": False,
                "message": "No image uploaded."
            }), 400

        file = request.files["image"]
        lang = request.form.get("lang", "en")
        print(f"Requested language: {lang}")

        tmp_name = None
        try:
            with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as tmp:
                file.save(tmp.name)
                tmp_name = tmp.name

            # Ở ngoài khối with, luồng ghi file tạm đã đóng hoàn toàn trên Windows
            ocr = get_ocr_instance(lang)
            result = ocr.predict(tmp_name)
        finally:
            if tmp_name and os.path.exists(tmp_name):
                os.remove(tmp_name)

        if not result or result[0] is None:
            return jsonify([])

        page = result[0]

        # 1. OCR -> OCRLine
        lines = convert_page(page)
        
        # 2. Gom Bubble
        bubbles = group_bubbles(lines)

        # 3. Sắp xếp thứ tự đọc
        bubbles = sort_bubbles(bubbles)
        print("========== RESULT =========")
        for b in bubbles:
            print(f"[{b.id}] {b.text}")

        response = build_response(bubbles)
        if response:
            print(type(response[0]["box"][0][0]))
        return jsonify(response)
    except Exception as e:
        print(f"Error in OCR: {e}")
        return jsonify([])


def create_mask_from_boxes(image, boxes):
    mask = np.zeros(image.shape[:2], dtype=np.uint8)
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    
    for box in boxes:
        # box là danh sách 4 điểm góc: [[x1, y1], [x2, y2], [x3, y3], [x4, y4]]
        pts = np.array(box, dtype=np.int32)
        rect = cv2.boundingRect(pts)
        x, y, w, h = rect
        if w <= 0 or h <= 0:
            continue
        
        # Đảm bảo không vượt quá biên ảnh
        y_min = max(0, y)
        y_max = min(image.shape[0], y+h)
        x_min = max(0, x)
        x_max = min(image.shape[1], x+w)
        
        h_crop = y_max - y_min
        w_crop = x_max - x_min
        if h_crop <= 0 or w_crop <= 0:
            continue
            
        local_poly_mask = np.zeros((h, w), dtype=np.uint8)
        local_pts = pts - [x, y]
        cv2.fillPoly(local_poly_mask, [local_pts], 255)
        
        gray_crop = gray[y_min:y_max, x_min:x_max]
        local_poly_mask_crop = local_poly_mask[0:h_crop, 0:w_crop]
        
        # Ngưỡng hóa Otsu nhị phân để bóc tách nét chữ
        _, thresh1 = cv2.threshold(gray_crop, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
        _, thresh2 = cv2.threshold(gray_crop, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        
        # Chọn ngưỡng có ít điểm trắng hơn trong vùng đa giác (chữ chiếm diện tích ít hơn nền)
        count1 = cv2.countNonZero(cv2.bitwise_and(thresh1, local_poly_mask_crop))
        count2 = cv2.countNonZero(cv2.bitwise_and(thresh2, local_poly_mask_crop))
        
        thresh = thresh1 if count1 < count2 else thresh2
        
        # Kết hợp mặt nạ đa giác và ngưỡng hóa nét chữ
        text_mask = cv2.bitwise_and(thresh, local_poly_mask_crop)
        
        # Giãn nở nhẹ mặt nạ nét chữ để che phủ hoàn toàn rìa chữ gốc
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
        text_mask = cv2.dilate(text_mask, kernel, iterations=1)
        
        # Hợp nhất vào mặt nạ toàn ảnh
        mask[y_min:y_max, x_min:x_max] = cv2.bitwise_or(mask[y_min:y_max, x_min:x_max], text_mask)
        
    return mask


@app.route("/inpaint", methods=["POST"])
def inpaint():
    print(">>> INPAINT CALLED <<<")
    try:
        if "image" not in request.files:
            return jsonify({
                "success": False,
                "message": "No image uploaded."
            }), 400

        file = request.files["image"]
        boxes_str = request.form.get("boxes", "[]")
        boxes = json.loads(boxes_str)
        print(f"Inpainting {len(boxes)} boxes...")

        # Đọc ảnh từ stream của Flask
        in_memory_file = io.BytesIO()
        file.save(in_memory_file)
        data = np.frombuffer(in_memory_file.getvalue(), dtype=np.uint8)
        img = cv2.imdecode(data, cv2.IMREAD_COLOR)

        if img is None:
            return jsonify({
                "success": False,
                "message": "Invalid image format."
            }), 400

        # Tạo mặt nạ dựa trên các bounding box
        mask = create_mask_from_boxes(img, boxes)

        # Thực hiện Inpainting xóa chữ gốc
        inpainted = cv2.inpaint(img, mask, 3, cv2.INPAINT_TELEA)

        # Encode ảnh đã inpaint về định dạng PNG để gửi lại Client
        _, buffer = cv2.imencode(".png", inpainted)
        io_buf = io.BytesIO(buffer)

        return send_file(io_buf, mimetype="image/png")
    except Exception as e:
        print(f"Error in Inpaint: {e}")
        return jsonify({
            "success": False,
            "message": str(e)
        }), 500


if __name__ == "__main__":
    app.run(
        host="127.0.0.1",
        port=5000,
        debug=True
    )