from flask import Flask, request, jsonify
from paddleocr import PaddleOCR

import tempfile
import os

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



if __name__ == "__main__":
    app.run(
        host="127.0.0.1",
        port=5000,
        debug=True
    )