from functools import cmp_to_key

from models import Bubble, ReadingStyle
from geometry import center_x, center_y


def compare_bubbles(
    a: Bubble,
    b: Bubble,
    style: ReadingStyle = ReadingStyle.MANGA
):
    """
    So sánh hai Bubble theo thứ tự đọcc.
    """

    ax = center_x(a)
    ay = center_y(a)

    bx = center_x(b)
    by = center_y(b)

    avg_height = (a.height + b.height) / 2

    # ----------------------------------------------------
    # Nếu khác hàng rõ rệt -> đọc từ trên xuống dưới
    # ----------------------------------------------------

    if abs(ay - by) > avg_height * 1.5:
        if ay < by:
            return -1
        else:
            return 1

    # ----------------------------------------------------
    # Cùng hàng
    # ----------------------------------------------------

    if style == ReadingStyle.MANGA:

        # Manga: phải -> trái

        if abs(ax - bx) > 10:

            if ax > bx:
                return -1
            else:
                return 1

    else:

        # Western: trái -> phải

        if abs(ax - bx) > 10:

            if ax < bx:
                return -1
            else:
                return 1

    # ----------------------------------------------------
    # fallback
    # ----------------------------------------------------

    if ay < by:
        return -1

    if ay > by:
        return 1

    return 0


def sort_bubbles(
    bubbles: list[Bubble],
    style: ReadingStyle = ReadingStyle.MANGA
) -> list[Bubble]:
    """
    Sắp xếp Bubble theo thứ tự đọc.
    """

    return sorted(
        bubbles,
        key=cmp_to_key(
            lambda a, b: compare_bubbles(a, b, style)
        )
    )