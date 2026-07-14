from models import OCRLine


def center_x(item):
    return (item.xmin + item.xmax) / 2


def center_y(item):
    return (item.ymin + item.ymax) / 2


def horizontal_gap(a, b):
    """
    Khoảng cách ngang giữa 2 box.
    Nếu chồng lên nhau thì = 0
    """
    return max(
        0,
        a.xmin - b.xmax,
        b.xmin - a.xmax
    )


def vertical_gap(a, b):
    """
    Khoảng cách dọc giữa 2 box.
    Nếu chồng lên nhau thì = 0
    """
    return max(
        0,
        a.ymin - b.ymax,
        b.ymin - a.ymax
    )


def overlap_x(a, b):
    """
    Hai box có giao nhau theo trục X hay không.
    """
    return max(a.xmin, b.xmin) < min(a.xmax, b.xmax)


def overlap_y(a, b):
    """
    Hai box có giao nhau theo trục Y hay không.
    """
    return max(a.ymin, b.ymin) < min(a.ymax, b.ymax)


def average_height(a, b):
    return (a.height + b.height) / 2


def average_width(a, b):
    return (a.width + b.width) / 2


def is_close(
    a: OCRLine,
    b: OCRLine,
    horizontal_factor=2.5,
    vertical_factor=1.5,
):
    """
    Hai OCRLine có đủ gần để xem là cùng một bubble hay không.
    """

    dx = horizontal_gap(a, b)
    dy = vertical_gap(a, b)

    avg_h = average_height(a, b)

    horizontal = overlap_x(a, b) or dx < avg_h * horizontal_factor
    vertical = dy < avg_h * vertical_factor

    return horizontal and vertical