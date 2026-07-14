from models import Bubble, OCRResponse


def bubble_to_response(
    bubble: Bubble
) -> OCRResponse:

    return OCRResponse(

        id=bubble.id,

        text=bubble.text,

        score=float(bubble.score),

        box=bubble.box
    )


def build_response(
    bubbles: list[Bubble]
) -> list[dict]:

    output = []

    for bubble in bubbles:

        item = bubble_to_response(bubble)

        output.append({

            "id": item.id,

            "text": item.text,

            "score": item.score,

            "box": item.box

        })

    return output