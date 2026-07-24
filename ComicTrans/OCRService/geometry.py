from models import OCRLine


def center_x(item):
    return (item.xmin + item.xmax) / 2


def center_y(item):
    return (item.ymin + item.ymax) / 2


def horizontal_gap(a, b):
    """
    Khoảng cách ngang giữa 2 boxs.
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


def overlap_x_ratio(a, b):
    """
    Tính tỷ lệ chồng lấp theo trục X so với chiều rộng của box nhỏ hơn.
    """
    overlap = max(0, min(a.xmax, b.xmax) - max(a.xmin, b.xmin))
    min_w = min(a.width, b.width)
    return overlap / min_w if min_w > 0 else 0


def overlap_y_ratio(a, b):
    """
    Tính tỷ lệ chồng lấp theo trục Y so với chiều cao của box nhỏ hơn.
    """
    overlap = max(0, min(a.ymax, b.ymax) - max(a.ymin, b.ymin))
    min_h = min(a.height, b.height)
    return overlap / min_h if min_h > 0 else 0


def is_close(
    a: OCRLine,
    b: OCRLine,
    horizontal_factor=1.2,
    vertical_factor=1.4,
    median_line_height=None
):
    """
    Hai OCRLine có đủ gần để xem là cùng một bubble hay không.
    Hỗ trợ chữ nằm ngang và chữ xếp dọc (Manga Nhật).
    """

    dx = horizontal_gap(a, b)
    dy = vertical_gap(a, b)

    # Nhận diện hướng của dòng chữ dựa trên tỉ lệ chiều rộng/cao
    a_is_vertical = a.height > a.width * 1.2
    b_is_vertical = b.height > b.width * 1.2

    # Sử dụng chiều cao/rộng trung vị hoặc trung bình tham chiếu
    ref_h = median_line_height if median_line_height else (a.height + b.height) / 2
    avg_w = (a.width + b.width) / 2

    if a_is_vertical and b_is_vertical:
        # CHỮ DỌC: Các cột xếp song song theo chiều ngang
        # Trực giao: Thẳng hàng dọc (chồng lấp Y > 15%) hoặc khoảng cách dọc cực kì nhỏ
        vertical = overlap_y_ratio(a, b) > 0.15 or dy < 5
        # Khoảng cách ngang giữa 2 cột dọc nhỏ hơn chiều rộng cột * factor
        horizontal = dx < avg_w * horizontal_factor
        return horizontal and vertical
    else:
        # CHỮ NGANG: Các dòng xếp chồng lên nhau theo chiều dọc
        # Trực giao: Thẳng hàng ngang (chồng lấp X > 15%) hoặc khoảng cách ngang cực kì nhỏ
        horizontal = overlap_x_ratio(a, b) > 0.15 or dx < 5
        # Khoảng cách dọc giữa các dòng ngang nhỏ hơn chiều cao dòng * factor
        vertical = dy < ref_h * vertical_factor
        return horizontal and vertical