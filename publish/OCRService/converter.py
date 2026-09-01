from models import OCRLine


def polygon_to_bbox(poly):
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]

    return (
        min(xs),
        min(ys),
        max(xs),
        max(ys)
    )


def convert_page(page):
    """
    Chuyển PaddleOCR page
    thành List[OCRLine]
    """

    lines = []

    texts = page["rec_texts"]
    scores = page["rec_scores"]
    polys = page["dt_polys"]

    for text, score, poly in zip(texts, scores, polys):

        xmin, ymin, xmax, ymax = map(float, polygon_to_bbox(poly))

        lines.append(
            OCRLine(
                text=text,
                score=float(score),

                box=[
                    [int(x), int(y)] 
                    for x, y in poly.tolist()
                ],

                xmin=xmin,
                ymin=ymin,
                xmax=xmax,
                ymax=ymax,

                width=xmax - xmin,
                height=ymax - ymin
            )
        )

    return lines