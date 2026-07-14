from dataclasses import dataclass
from typing import List
from enum import Enum


class ReadingStyle(Enum):
    MANGA = "manga"
    WESTERN = "western"


@dataclass
class OCRLine:
    text: str
    score: float

    box: list

    xmin: float
    ymin: float
    xmax: float
    ymax: float

    width: float
    height: float


@dataclass
class Bubble:
    id: int

    lines: List[OCRLine]

    text: str
    score: float

    box: list

    xmin: float
    ymin: float
    xmax: float
    ymax: float

    width: float
    height: float


@dataclass
class OCRResponse:
    id: int
    text: str
    score: float
    box: list