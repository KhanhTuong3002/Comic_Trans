from paddleocr import PaddleOCR

from converter import convert_page
from bubble_group import group_bubbles
from reading_order import sort_bubbles
from response import build_response

ocr = PaddleOCR(
    lang="en",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=False
)

page = ocr.predict("test.png")[0]

lines = convert_page(page)

bubbles = group_bubbles(lines)

ordered = sort_bubbles(bubbles)

response = build_response(ordered)

print(response[:3])