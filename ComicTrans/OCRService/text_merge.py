from models import OCRLine


def merge_lines(lines: list[OCRLine]) -> str:
    """
    Ghép nhiều OCRLine thành một đoạn văn.

    Giữ xuống dòng.
    Xử lý dấu gạch nối.
    Loại bỏ khoảng trắng dư.
    """

    if not lines:
        return ""

    # Sắp xếp từ trên xuống dưới
    ordered = sorted(
        lines,
        key=lambda x: (x.ymin, x.xmin)
    )

    result = []

    for line in ordered:

        text = line.text.strip()

        if not text:
            continue

        if not result:
            result.append(text)
            continue

        previous = result[-1]

        # -----------------------------
        # Nếu dòng trước kết thúc bằng -
        # thì nối luôn
        # -----------------------------
        if previous.endswith("-"):

            result[-1] = previous[:-1] + text

        else:

            result.append(text)

    return "\n".join(result)