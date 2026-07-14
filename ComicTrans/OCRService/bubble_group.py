from collections import defaultdict

from models import OCRLine, Bubble
from geometry import is_close
from text_merge import merge_lines


def group_bubbles(lines: list[OCRLine]) -> list[Bubble]:
    """
    Gom các OCRLine thành Bubble bằng Union-Find.
    """

    if not lines:
        return []

    n = len(lines)
    parent = list(range(n))

    # ==========================
    # Union Find
    # ==========================

    def find(x):
        if parent[x] != x:
            parent[x] = find(parent[x])
        return parent[x]

    def union(a, b):
        ra = find(a)
        rb = find(b)

        if ra != rb:
            parent[rb] = ra

    # ==========================
    # Gom nhóm
    # ==========================

    for i in range(n):
        for j in range(i + 1, n):
            if is_close(lines[i], lines[j]):
                union(i, j)

    # ==========================
    # Chia thành các group
    # ==========================

    groups = defaultdict(list)

    for i in range(n):
        groups[find(i)].append(lines[i])

    # ==========================
    # Sinh Bubble
    # ==========================

    bubbles = []
    bubble_id = 1

    for group in groups.values():

        # Sắp xếp các dòng trong bubble
        group.sort(key=lambda x: (x.ymin, x.xmin))

        xmin = min(x.xmin for x in group)
        ymin = min(x.ymin for x in group)
        xmax = max(x.xmax for x in group)
        ymax = max(x.ymax for x in group)

        score = sum(x.score for x in group) / len(group)

        text = merge_lines(group)

        bubble = Bubble(
            id=bubble_id,
            lines=group,
            text=text,
            score=score,
            box=[
                [int(xmin), int(ymin)],
                [int(xmax), int(ymin)],
                [int(xmax), int(ymax)],
                [int(xmin), int(ymax)]
            ],
            xmin=xmin,
            ymin=ymin,
            xmax=xmax,
            ymax=ymax,
            width=xmax - xmin,
            height=ymax - ymin
        )

        bubbles.append(bubble)
        bubble_id += 1

    return bubbles