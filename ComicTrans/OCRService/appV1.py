"""from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
import tempfile
import os

app = Flask(__name__)

ocr = PaddleOCR(
    lang="en",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=False
)


@app.route("/ocr1", methods=["POST"])
def recognize():

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

    texts = page["rec_texts"]
    scores = page["rec_scores"]
    polys = page["dt_polys"]

    output = []

    for text, score, poly in zip(texts, scores, polys):

        output.append({
            "text": text,
            "score": float(score),
            "box": poly.tolist()
        })

    return jsonify(output)


if __name__ == "__main__":
    app.run(port=5000)