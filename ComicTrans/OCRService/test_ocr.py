from paddleocr import PaddleOCR
from pprint import pprint

ocr = PaddleOCR(
    lang="en",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=False
)

result = ocr.predict("temp.png")

print(type(result))
print(type(result[0]))

print(result[0].keys())