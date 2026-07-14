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

ocr = PaddleOCR(
    lang="en",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=False
)


@app.route("/ocr", methods=["POST"])
def recognize():
    print(">>> RECOGNIZE CALLED <<<")

    if "image" not in request.files:
        return jsonify({
            "success": False,
            "message": "No image uploaded."
        }), 400

    file = request.files["image"]

    with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as tmp:

        file.save(tmp.name)

        result = ocr.predict(tmp.name)

    os.remove(tmp.name)

    page = result[0]

    # 1. OCR -> OCRLine
    lines = convert_page(page)
    print(f"OCR Lines: {len(lines)}")

    # 2. Gom Bubble
    bubbles = group_bubbles(lines)
    print(f"Bubbles: {len(bubbles)}")

    # 3. Sắp xếp thứ tự đọc
    bubbles = sort_bubbles(bubbles)
    print("========== RESULT ==========")
    for b in bubbles:
        print(f"[{b.id}] {b.text}")

    response = build_response(bubbles)

    # 4. Bubble -> JSON  
    response = build_response(bubbles)
    print(type(response[0]["box"][0][0]))
    return jsonify(response)



if __name__ == "__main__":
    app.run(
        host="127.0.0.1",
        port=5000,
        debug=True
    )