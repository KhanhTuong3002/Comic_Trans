from models import Bubble


def build_response(bubbles: list[Bubble]):

    output = []

    for bubble in bubbles:

        output.append({

            "id": int(bubble.id),

            "text": bubble.text,

            "score": float(bubble.score),

            "box": [
                [int(x), int(y)]
                for x, y in bubble.box
            ]

        })

    return output