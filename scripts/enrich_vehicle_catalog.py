#!/usr/bin/env python3
"""Enrich vehicle-catalog.json from unmatched brands/models export."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG_PATH = ROOT / "Lots.Application" / "Data" / "vehicle-catalog.json"
EXPORT_PATH = ROOT / "Lots.Application" / "Data" / "unmatched-export.json"

# Raw brand string -> existing catalog canonical
BRAND_REDIRECT: dict[str, str] = {
    "lada": "Lada (ВАЗ)",
    "ваз": "Lada (ВАЗ)",
    "vaz": "Lada (ВАЗ)",
    "exceed": "Exeed",
    "хендэ": "Hyundai",
    "хайма": "Haima",
    "haima": "Haima",
    "dongfeng": "Dongfeng",
    "donfeng": "Dongfeng",
    "dongfeen": "Dongfeng",
    "dfm": "Dongfeng",
    "dfsk": "Dongfeng",
    "dfsk": "Dongfeng",
    "li fan": "Lifan",
    "lifan": "Lifan",
    "landmark": "Landmark",
    "tenet": "Tenet",
    "tank": "Tank",
    "voyah": "Voyah",
    "aito": "AITO",
    "zeekr": "Zeekr",
    "lixiang": "Li Auto",
    "lixiang": "Li Auto",
    "hongqi": "Hongqi",
    "xcite": "Xcite",
    "maple": "Maple",
    "forthing": "Forthing",
    "soueast": "Soueast",
    "chng": "Changan",
    "hawtai": "Hawtai",
    "hafei": "Hafei",
    "range rover": "Land Rover",
    "ровер": "Land Rover",
    "роver": "Land Rover",
    "derways": "Derways",
    "дервейс": "Derways",
    "uni-s": "Changan",
    "volga": "Volga",
    "иж": "ИЖ",
    "кo": "КО",
    "сеаз": "SEAZ",
    "бagem": "BAW",
}

# New brands: normalized key -> canonical display name
NEW_BRAND_CANONICAL: dict[str, str] = {
    "богдан": "Bogdan",
    "bogdan": "Bogdan",
    "swm": "SWM",
    "gac": "GAC",
    "zotye": "Zotye",
    "mg": "MG",
    "mini": "Mini",
    "brilliance": "Brilliance",
    "livan": "Livan",
    "byd": "BYD",
    "ravon": "Ravon",
    "acura": "Acura",
    "saab": "Saab",
    "genesis": "Genesis",
    "seat": "SEAT",
    "maserati": "Maserati",
    "iran khodro": "Iran Khodro",
    "smart": "Smart",
    "wey": "WEY",
    "dacia": "Dacia",
    "isuzu": "Isuzu",
    "alfa romeo": "Alfa Romeo",
    "bentley": "Bentley",
    "saturn": "Saturn",
    "hummer": "Hummer",
    "dongfeng": "Dongfeng",
    "landmark": "Landmark",
    "tenet": "Tenet",
    "tank": "Tank",
    "voyah": "Voyah",
    "aito": "AITO",
    "zeekr": "Zeekr",
    "lixiang": "Li Auto",
    "hongqi": "Hongqi",
    "xcite": "Xcite",
    "maple": "Maple",
    "forthing": "Forthing",
    "soueast": "Soueast",
    "hawtai": "Hawtai",
    "hafei": "Hafei",
    "haima": "Haima",
    "derways": "Derways",
    "иж": "ИЖ",
    "seaz": "SEAZ",
    "baw": "BAW",
    "бagem": "BAW",
}

LADA_KEYWORDS: dict[str, str] = {
    "samara": "Samara",
    "самara": "Samara",
    "самara": "Samara",
    "priora": "Priora",
    "приора": "Priora",
    "kalina": "Kalina",
    "кalina": "Kalina",
    "кalina": "Kalina",
    "granta": "Granta",
    "гранта": "Granta",
    "vesta": "Vesta",
    "niva": "Niva",
    "4x4": "Niva",
    "4х4": "Niva",
    "largus": "Largus",
    "xray": "XRAY",
    "x-ray": "XRAY",
}

BMW_SERIES: dict[str, str] = {
    "116": "1 Series",
    "118": "1 Series",
    "120": "1 Series",
    "316": "3 Series",
    "318": "3 Series",
    "320": "3 Series",
    "323": "3 Series",
    "325": "3 Series",
    "328": "3 Series",
    "330": "3 Series",
    "520": "5 Series",
    "523": "5 Series",
    "525": "5 Series",
    "528": "5 Series",
    "530": "5 Series",
    "535": "5 Series",
}

SKIP_BRAND_PATTERNS = re.compile(
    r"^\d{4,}|^mkkf|^2824|^2716|^3009|^м214100|^af$",
    re.IGNORECASE,
)


def norm_key(value: str) -> str:
    return " ".join(value.strip().split()).lower()


def normalize_lookup_key(value: str) -> str:
    return " ".join(value.strip().split())


def strip_canonical_dup_aliases(entry: dict) -> None:
    canonical = entry["canonical"]
    entry["aliases"] = [
        a
        for a in entry.get("aliases", [])
        if norm_key(a) != norm_key(canonical)
    ]


def build_brand_index(catalog: dict) -> dict[str, str]:
    index: dict[str, str] = {}
    for brand in catalog["brands"]:
        canonical = brand["canonical"]
        index[norm_key(canonical)] = canonical
        for alias in brand.get("aliases", []):
            index[norm_key(alias)] = canonical
    for key, canonical in BRAND_REDIRECT.items():
        index[norm_key(key)] = canonical
    return index


def find_brand_entry(catalog: dict, canonical: str) -> dict | None:
    for brand in catalog["brands"]:
        if brand["canonical"].lower() == canonical.lower():
            return brand
    return None


def ensure_brand(catalog: dict, brand_index: dict, raw_brand: str) -> str | None:
    key = norm_key(raw_brand)
    if key in brand_index:
        return brand_index[key]

    if SKIP_BRAND_PATTERNS.search(raw_brand.strip()):
        return None

    if key in NEW_BRAND_CANONICAL:
        canonical = NEW_BRAND_CANONICAL[key]
    else:
        canonical = raw_brand.strip()
        if canonical.isupper() and len(canonical) > 3:
            canonical = canonical.title()

    brand = {"canonical": canonical, "aliases": [], "models": []}
    if norm_key(canonical) != key:
        brand["aliases"].append(raw_brand.strip())
    catalog["brands"].append(brand)
    brand_index[key] = canonical
    brand_index[norm_key(canonical)] = canonical
    return canonical


def model_known(brand: dict, model_raw: str) -> bool:
    key = norm_key(model_raw)
    for model in brand.get("models", []):
        if norm_key(model["canonical"]) == key:
            return True
        for alias in model.get("aliases", []):
            if norm_key(alias) == key:
                return True
    return False


def find_model_entry(brand: dict, canonical: str) -> dict | None:
    for model in brand.get("models", []):
        if model["canonical"].lower() == canonical.lower():
            return model
    return None


def ensure_model(brand: dict, canonical: str) -> dict:
    existing = find_model_entry(brand, canonical)
    if existing:
        return existing
    model = {"canonical": canonical, "aliases": []}
    brand.setdefault("models", []).append(model)
    return model


def add_model_alias(brand: dict, target_canonical: str, alias: str) -> bool:
    alias = normalize_lookup_key(alias)
    if not alias:
        return False
    if norm_key(alias) == norm_key(target_canonical):
        return False
    if model_known(brand, alias):
        return False
    model = ensure_model(brand, target_canonical)
    model["aliases"].append(alias)
    return True


def add_new_model(brand: dict, canonical: str, alias: str | None = None) -> bool:
    canonical = normalize_lookup_key(canonical)
    if not canonical or model_known(brand, canonical):
        return False
    if alias and norm_key(alias) != norm_key(canonical):
        ensure_model(brand, canonical)["aliases"].append(normalize_lookup_key(alias))
    else:
        ensure_model(brand, canonical)
    return True


def resolve_lada_model(model_raw: str) -> tuple[str, str | None]:
    """Return (canonical, alias_to_add). alias may be full raw string."""
    raw = normalize_lookup_key(model_raw)
    lower = raw.lower()

    for keyword, canonical in LADA_KEYWORDS.items():
        if keyword in lower:
            return canonical, raw

    code_match = re.search(r"\b(\d{4,6})\b", raw)
    if code_match:
        code = code_match.group(1)
        prefix4 = code[:4]
        prefix3 = code[:3]

        if prefix4 in {"2114", "2115", "2113"} or code.startswith("2114") or code.startswith("2115"):
            return "Samara", raw
        if prefix4 == "2112" or code.startswith("2112"):
            return "2112", raw
        if prefix4 == "2110" or code.startswith("2110"):
            return "2110", raw
        if prefix4 == "2111" or code.startswith("2111"):
            return "1111", raw
        if code.startswith("1117") or code.startswith("1118") or code.startswith("1119") or code.startswith("111"):
            if "1111" in code[:4]:
                return "1111", raw
            return "Kalina", raw
        if code.startswith("217"):
            return "Priora", raw
        if code.startswith("219"):
            return "Granta", raw
        if code.startswith("2121") or code.startswith("2123") or code.startswith("2131"):
            return "Niva", raw
        if code.startswith("21214") or code.startswith("212140"):
            return "21214", raw
        if code.startswith("21213"):
            return "21213", raw
        if len(code) >= 4:
            return code[:4] if len(code) > 4 else code, raw

    if re.fullmatch(r"\d{4,6}", raw):
        return raw[:4] if len(raw) > 4 else raw, None

    return raw, None


def resolve_bmw_model(model_raw: str) -> tuple[str | None, str]:
    raw = normalize_lookup_key(model_raw)
    match = re.match(r"^(\d{3})[a-zA-Z]?", raw.replace(" ", ""), re.IGNORECASE)
    if match:
        prefix = match.group(1)
        for digits, series in BMW_SERIES.items():
            if prefix.startswith(digits):
                return series, raw
    if raw.upper().startswith("X"):
        x_match = re.match(r"^(X\d+[a-zA-Z]*)", raw.replace(" ", ""), re.IGNORECASE)
        if x_match:
            return x_match.group(1).upper(), raw
    return None, raw


def resolve_renault_model(model_raw: str) -> tuple[str | None, str]:
    raw = normalize_lookup_key(model_raw)
    lower = raw.lower()
    if lower in {"sr", "logan sr", "logan stepway", "logan (sr)"} or "logan" in lower:
        if "stepway" in lower:
            return "Sandero Stepway", raw
        return "Logan", raw
    if "sandero" in lower:
        return "Sandero", raw
    if "duster" in lower:
        return "Duster", raw
    if "megane" in lower or "scenic" in lower:
        return "Megane", raw
    if "kangoo" in lower:
        return "Kangoo", raw
    if "koleos" in lower:
        return "Koleos", raw
    if "логан" in lower:
        return "Logan", raw
    return None, raw


def resolve_model(brand_canonical: str, brand: dict, model_raw: str) -> bool:
    if model_known(brand, model_raw):
        return False

    raw = normalize_lookup_key(model_raw)
    lower = raw.lower()

    if brand_canonical == "Lada (ВАЗ)":
        canonical, alias = resolve_lada_model(raw)
        if alias:
            return add_model_alias(brand, canonical, alias)
        return add_new_model(brand, canonical)

    if brand_canonical == "BMW":
        series, alias = resolve_bmw_model(raw)
        if series:
            return add_model_alias(brand, series, alias)
        return add_new_model(brand, raw)

    if brand_canonical == "Renault":
        target, alias = resolve_renault_model(raw)
        if target:
            return add_model_alias(brand, target, alias)
        return add_new_model(brand, raw)

    # Substring match against existing canonicals (longest first)
    best: str | None = None
    for model in brand.get("models", []):
        c = model["canonical"]
        c_lower = c.lower()
        if c_lower in lower or lower in c_lower:
            if best is None or len(c) > len(best):
                best = c
    if best:
        return add_model_alias(brand, best, raw)

    # Known cross-brand patterns
    if brand_canonical == "Chevrolet":
        if "niva" in lower or "212300" in lower:
            return add_model_alias(brand, "Niva", raw)
        if "lacetti" in lower or "klan" in lower or "klau" in lower:
            return add_model_alias(brand, "Lacetti", raw)
        if "aveo" in lower or "klas" in lower or "klit" in lower:
            return add_model_alias(brand, "Aveo", raw)
        if "spark" in lower:
            return add_model_alias(brand, "Spark", raw) or add_new_model(brand, "Spark", raw)

    if brand_canonical == "Geely" and "mk" in lower.replace("-", "").replace(" ", ""):
        return add_model_alias(brand, "MK", raw)

    if brand_canonical == "Chery":
        if "tiggo" in lower or "t11" in lower or "suv t11" in lower:
            if "9" in lower:
                return add_model_alias(brand, "Tiggo 9", raw)
            if "8" in lower:
                return add_model_alias(brand, "Tiggo 8", raw)
            if "7" in lower:
                return add_model_alias(brand, "Tiggo 7", raw)
            if "5" in lower or "t21" in lower:
                return add_model_alias(brand, "Tiggo 5", raw)
            if "4" in lower or "t19" in lower or "cross" in lower:
                return add_model_alias(brand, "Tiggo 4", raw) or add_model_alias(brand, "Tiggo Cross", raw)
            if "3" in lower:
                return add_model_alias(brand, "Tiggo 3", raw)
            if "2" in lower:
                return add_model_alias(brand, "Tiggo 2", raw)
            return add_model_alias(brand, "Tiggo (T11)", raw)
        if lower.startswith("a1") or lower.startswith("a2"):
            return add_new_model(brand, raw)

    if brand_canonical == "Hyundai" and "accent" in lower or "акцент" in lower:
        return add_model_alias(brand, "Solaris", raw)

    if brand_canonical == "Kia":
        if "optima" in lower or " k5" in lower or lower.endswith("jf"):
            return add_model_alias(brand, "K5", raw)
        if "ceed" in lower or "cee'd" in lower or "ed (" in lower or "jd (" in lower:
            return add_model_alias(brand, "Ceed", raw)
        if "rio" in lower or "рio" in lower or "рио" in lower:
            return add_model_alias(brand, "Rio", raw)
        if "cerato" in lower or "forte" in lower:
            return add_model_alias(brand, "Cerato", raw)

    if brand_canonical == "Volkswagen" and "passat cc" in lower:
        return add_model_alias(brand, "Passat", raw)

    if brand_canonical == "Land Rover":
        if "discovery 3" in lower:
            return add_model_alias(brand, "Discovery", raw)
        if "freelander 2" in lower:
            return add_model_alias(brand, "Freelander", raw)

    if brand_canonical == "Mercedes-Benz":
        for cls in ("GLA", "GLC", "GLE", "GLK", "GLS", "GL", "ML", "E ", "C ", "S ", "V "):
            if cls.lower().strip() in lower or lower.startswith(cls.lower().strip()):
                mapping = {
                    "gla": "GLA",
                    "glc": "GLC",
                    "gle": "GLE",
                    "glk": "GLK",
                    "gls": "GLS",
                    "gl ": "GL-Class",
                    "ml": "M-Class",
                    "e ": "E-Class",
                    "c ": "C-Class",
                    "s ": "S-Class",
                    "v ": "Viano",
                }
                for k, v in mapping.items():
                    if lower.startswith(k.strip()) or f" {k.strip()} " in f" {lower} ":
                        return add_model_alias(brand, v, raw)

    return add_new_model(brand, raw)


def add_brand_alias(catalog: dict, brand_index: dict, raw_brand: str) -> bool:
    canonical = brand_index.get(norm_key(raw_brand))
    if not canonical:
        return False
    brand = find_brand_entry(catalog, canonical)
    if not brand:
        return False
    alias = normalize_lookup_key(raw_brand)
    if norm_key(alias) == norm_key(canonical):
        return False
    existing = {norm_key(a) for a in brand.get("aliases", [])}
    if norm_key(alias) in existing:
        return False
    brand.setdefault("aliases", []).append(alias)
    brand_index[norm_key(alias)] = canonical
    return True


def main() -> int:
    if not EXPORT_PATH.exists():
        print(f"Missing export file: {EXPORT_PATH}", file=sys.stderr)
        return 1

    export = json.loads(EXPORT_PATH.read_text(encoding="utf-8-sig"))
    catalog = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))

    brand_index = build_brand_index(catalog)
    brands_added = 0
    brand_aliases_added = 0
    models_updated = 0
    skipped_brands: list[str] = []

    for item in export.get("brands", []):
        raw = item["brand"]
        if ensure_brand(catalog, brand_index, raw):
            if find_brand_entry(catalog, brand_index[norm_key(raw)]) and item.get("count"):
                brands_added += 1
        elif add_brand_alias(catalog, brand_index, raw):
            brand_aliases_added += 1
        elif norm_key(raw) not in brand_index:
            skipped_brands.append(raw)

    for item in export.get("models", []):
        raw_brand = item["brand"]
        raw_model = item["model"]
        canonical_brand = ensure_brand(catalog, brand_index, raw_brand)
        if not canonical_brand:
            continue
        brand = find_brand_entry(catalog, canonical_brand)
        if not brand:
            continue
        if resolve_model(canonical_brand, brand, raw_model):
            models_updated += 1

    for brand in catalog["brands"]:
        strip_canonical_dup_aliases(brand)
        for model in brand.get("models", []):
            strip_canonical_dup_aliases(model)

    catalog["brands"].sort(key=lambda b: b["canonical"].lower())
    for brand in catalog["brands"]:
        brand["models"].sort(key=lambda m: m["canonical"].lower())
        for model in brand["models"]:
            model["aliases"] = sorted(set(model["aliases"]), key=str.lower)

    CATALOG_PATH.write_text(
        json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    print(f"Brand entries created/ensured: {brands_added}")
    print(f"Brand aliases added: {brand_aliases_added}")
    print(f"Model updates: {models_updated}")
    if skipped_brands:
        print(f"Skipped suspicious brands ({len(skipped_brands)}): {skipped_brands[:10]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
