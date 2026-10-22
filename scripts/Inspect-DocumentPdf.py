"""Read-only real-render acceptance checks. Dev tools: pypdf, pypdfium2, Pillow.

Usage: python scripts/Inspect-DocumentPdf.py artifacts/document-acceptance/<run>
Renders pages beside the synthetic acceptance output; never edits a PDF.
"""
import json
import sys
from pathlib import Path

import pypdfium2 as pdfium
from pypdf import PdfReader

root = Path(sys.argv[1]).resolve(strict=True)
pdfs = list(root.rglob("Fidelity 文 spaced.pdf"))
if not pdfs:
    raise SystemExit("No real-engine fidelity output found; run LibreOfficeAcceptanceTests first.")
results = []
for path in pdfs:
    reader = PdfReader(path, strict=True)
    assert len(reader.pages) == 2, "Explicit page break/A4 pagination changed"
    texts = [page.extract_text() for page in reader.pages]
    assert all(value in texts[0] for value in ["FilePilot fidelity acceptance", "Format", "Result", "DOCX", "PDF offline"])
    assert "Second page: preserved layout" in texts[1]
    assert "文件转换" in texts[1], "Unicode text did not survive export"
    assert sum(len(page.images) for page in reader.pages) >= 1, "Embedded image missing"
    for page in reader.pages:
        assert abs(float(page.mediabox.width) - 595.3) < 2
        assert abs(float(page.mediabox.height) - 841.9) < 2
    metadata = dict(reader.metadata or {})
    for key in ["/Author", "/Title", "/Subject", "/Keywords"]:
        assert not metadata.get(key), f"Source property leaked: {key}"
    assert "PRIVATE_" not in str(metadata)
    assert "PRIVATE_" not in str(reader.xmp_metadata)
    # Check decoded PDF streams too; compressed content cannot be checked by raw byte search.
    for page in reader.pages:
        assert b"PRIVATE_" not in page.get_contents().get_data()
    document = pdfium.PdfDocument(path)
    renders = []
    try:
        for index in range(len(document)):
            page = document[index]
            bitmap = page.render(scale=1.25)
            try:
                image = bitmap.to_pil()
                output = path.with_name(f"page-{index + 1}.png")
                image.save(output)
                renders.append(str(output))
            finally:
                bitmap.close()
                page.close()
    finally:
        document.close()
    results.append({"pdf": str(path), "pages": 2, "metadata": metadata, "renders": renders})
report = root / "pdf-inspection.json"
report.write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
print(f"PASS: {len(results)} real PDF(s): pagination, A4, text, Unicode, image and property checks. Report: {report}")
