"""Unity Web(WebGL) 빌드 폴더를 itch.io에 올릴 zip으로 만든다.

PowerShell의 Compress-Archive를 쓰면 안 된다. 경로 구분자를 역슬래시로 써서
ZIP 스펙을 어기는데, itch.io는 그걸 폴더로 인식하지 못해 Build/·TemplateData/의
모든 파일이 404가 난다. 화면은 뜨는데 게임만 안 나오는, 원인 찾기 고약한 증상이다.

  python Tools/pack_web_build.py            # Build/ → seekaboo-web.zip
  python Tools/pack_web_build.py 다른폴더 출력.zip

index.html이 zip 최상단에 와야 한다. 폴더를 통째로 감싸면 itch.io가 진입점을 못 찾는다.
"""

import os
import sys
import zipfile

REQUIRED = ["index.html", "Build", "TemplateData"]


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "Build"
    out = sys.argv[2] if len(sys.argv) > 2 else "seekaboo-web.zip"

    if not os.path.isdir(src):
        sys.exit("빌드 폴더가 없습니다: %s\n먼저 Unity에서 Build Profiles → Web → Build 를 하세요." % src)

    missing = [n for n in REQUIRED if not os.path.exists(os.path.join(src, n))]
    if missing:
        sys.exit(
            "%s 안에 %s 가 없습니다.\n"
            "빌드 결과 폴더(안에 index.html이 있는 폴더)를 지정하세요." % (src, ", ".join(missing))
        )

    total = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(src):
            for name in files:
                path = os.path.join(root, name)
                # 슬래시로 정규화한 상대 경로. 이게 이 스크립트의 존재 이유다.
                arc = os.path.relpath(path, src).replace(os.sep, "/")
                z.write(path, arc)
                total += os.path.getsize(path)

    print("만듦: %s" % os.path.abspath(out))
    print("원본 %.1f MB → zip %.1f MB" % (total / 1048576.0, os.path.getsize(out) / 1048576.0))

    # 올리기 전에 최상단에 index.html이 있는지 눈으로 확인한다.
    with zipfile.ZipFile(out) as z:
        names = z.namelist()
    print("항목 %d개, 최상단 index.html %s" % (len(names), "있음" if "index.html" in names else "⚠ 없음"))
    if any("\\" in n for n in names):
        print("⚠ 역슬래시가 섞였습니다 — itch.io에서 404가 납니다.")


if __name__ == "__main__":
    main()
