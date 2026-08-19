from __future__ import annotations

import csv
from pathlib import Path


# index|view|pose|visibility|sharpness|notes
ANNOTATIONS = """
1|left three-quarter|eating crouch|full body|sharp|left face, ear, shoulder band and forepaws
2|right/top three-quarter|eating crouch|mostly visible|soft|right flank and dorsal boundary
3|front/top|eating crouch|full body|sharp|head stripe, ears and forepaws
4|front/top|eating crouch|full body|sharp|head width and compact body
5|front/top|eating crouch|full body|sharp|front symmetry and white back
6|front/top|eating crouch|full body|sharp|nose and crown
7|front/top|eating crouch|full body|sharp|front paws and head angle
8|right three-quarter|alert crouch|full body|sharp|right cheek and chest
9|right three-quarter|alert crouch|full body|sharp|pink nose profile
10|front/top|alert crouch|full body|sharp|crown and shoulder band
11|front/top|alert crouch|full body|sharp|ear spacing and dorsal patch
12|left side|eating crouch|full body|sharp|strong left profile reference
13|front/top|eating crouch|full body|sharp|compact silhouette
14|front/top|eating crouch|full body|sharp|head-to-body ratio
15|left side|sitting on hand|mostly visible|soft|belly, hind leg and side coat transition
16|left side|standing/sniffing|full body|sharp|primary left profile and proportions
17|left side|standing/sniffing|full body|sharp|white band and dorsal island
18|front three-quarter|walking/sniffing|full body|sharp|face, eyes and gait posture
19|front three-quarter|walking/sniffing|full body|sharp|nose and forepaw placement
20|front|upright at enclosure edge|upper body|sharp|frontal face, ears and paws
21|right three-quarter|upright at enclosure edge|upper body|sharp|right face and chest
22|right side/top|standing tall|full body|sharp|elongated body mechanics
23|left side|eating crouch|full body|sharp|left face and shoulder
24|rear/right side|walking|full body|sharp|rear coat boundary and rump
25|front/left three-quarter|upright|mostly visible|sharp|front face and white chest
26|left side|sitting in bowl|full body|sharp|clean left silhouette and scale
27|front|upright at enclosure edge|upper body|sharp|frontal eyes, nose and ears
28|front|upright at enclosure edge|upper body|sharp|front symmetry
29|left close-up|upright/sniffing|partial body|soft|eye, ear and muzzle detail
30|left close-up|eating|partial body|sharp|fur texture, ear and paw detail
31|front|eating crouch|full body|sharp|natural head-down posture
32|right/top|walking/eating|full body|sharp|right flank and dorsal markings
33|right/top|walking/eating|full body|sharp|right outline and rump
34|right/top|walking|full body|sharp|long body and white band
35|right/top|walking|full body|sharp|coat placement across back
36|left side|standing/sniffing|full body|sharp|primary neutral left profile
37|top/right|walking while touched|full body|sharp|top coat map and scale
38|rear/right|upright climbing|full body|sharp|rear anatomy and band boundary
39|rear/right|upright climbing|full body|sharp|rump shape and hind paws
40|rear|upright climbing|full body|sharp|rear coat and tail area
41|right side|upright climbing|full body|sharp|right profile while stretched
42|top/rear|upright climbing|full body|sharp|complete dorsal coat map
43|front/left|walking|mostly visible|sharp|face and left forepaw
44|top/left|walking|full body|sharp|crown and dorsal marking
45|top/right|walking|full body|sharp|right ear and white band
46|front close-up|sniffing|partial body|sharp|eye, nose, whiskers and facial fur
47|front three-quarter|standing/sniffing|full body|sharp|neutral face and body
48|top/right|walking|full body|sharp|top markings and body width
49|rear/top|upright climbing|full body|sharp|rear silhouette and dorsal map
50|rear/right|upright climbing|full body|sharp|rump, tail and right ear
51|top/right|walking|full body|sharp|right side and coat transition
52|top/front|eating|full body|sharp|head-down posture and paws
53|top/front|eating|full body|sharp|dorsal patch and body footprint
54|right side|standing in bowl|full body|sharp|right neutral profile
55|front|upright at bowl|upper body|sharp|frontal head, paws and chest
56|right side/top|standing at bowl|full body|sharp|right outline and band
57|top/right|walking|full body|sharp|top/right coat map
58|front three-quarter|walking/sniffing|full body|sharp|face identity and compact proportions
59|top/right|walking|full body|sharp|right ear and crown
60|top/rear|sitting/grooming|full body|sharp|rounded rump and white back
61|top/right|walking/sniffing|mostly visible|sharp|top face and shoulder
62|left side|upright at bowl|full body|sharp|left face, chest and paw
63|left side|upright at bowl|full body|sharp|left eye and body balance
64|front|upright at bowl|upper body|sharp|front face and both paws
65|front/left|walking|full body|sharp|head-down gait and shoulders
66|front/left|walking|full body|sharp|forepaw positions and muzzle
67|front/left|walking|full body|sharp|desk locomotion reference
68|left side|walking|full body|sharp|extended left gait silhouette
69|left side|standing/sniffing|full body|sharp|clean left identity reference
70|top/left|walking|full body|soft|movement mechanics and dorsal marking
71|top/left|walking|full body|sharp|white band width and rump
72|front/left|walking near house|full body|soft|motion blur reference only
73|top/rear|walking near house|full body|soft|dorsal pattern despite blur
74|top/front|walking near house|mostly visible|soft|head-to-back transition
75|rear/top|entering house|partial body|soft|rump and hind movement
76|top/front|eating crouch|full body|sharp|head-down body footprint
77|front/underside|held upright|full body|sharp|belly, limb length and pink paws
78|right side|held horizontally|full body|sharp|right profile and underside
79|left/underside|held upright|full body|sharp|belly, hind foot and body depth
80|top/right|walking while touched|full body|sharp|top coat map and tactile response
81|front/right|being petted|upper body|sharp|petting response, face and ear
""".strip()


def main() -> None:
    catalog_path = Path("pet-art/references/reference_catalog.csv")
    annotations: dict[int, list[str]] = {}
    for line in ANNOTATIONS.splitlines():
        parts = line.split("|", 5)
        annotations[int(parts[0])] = parts[1:]
    if set(annotations) != set(range(1, 82)):
        raise SystemExit("Annotations must cover every source image exactly once")

    with catalog_path.open("r", newline="", encoding="utf-8-sig") as handle:
        rows = list(csv.DictReader(handle))
    if len(rows) != 81:
        raise SystemExit(f"Expected 81 catalog rows, got {len(rows)}")

    for row in rows:
        view, pose, visibility, sharpness, notes = annotations[int(row["index"])]
        row.update(
            view=view,
            pose=pose,
            visibility=visibility,
            sharpness=sharpness,
            notes=notes,
        )

    with catalog_path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    print("Annotated all 81 reference photos")


if __name__ == "__main__":
    main()
