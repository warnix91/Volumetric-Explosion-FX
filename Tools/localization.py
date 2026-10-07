import json
import re
from pathlib import Path

LANGUAGES = ('en-us', 'fr-fr', 'de-de', 'es-es', 'it-it', 'pt-br', 'ru', 'ja', 'zh-cn')
CALL = re.compile(r'(?:\bL|\btext\.Get|\btext\.Format)\(\s*"([^"\\]+)"\s*,\s*("(?:\\.|[^"\\])*")')


def validate_localization(root):
    root = Path(root)
    expected = {}
    for source in (root / 'Source').rglob('*.cs'):
        for match in CALL.finditer(source.read_text(encoding='utf-8')):
            key, value = match.group(1), json.loads(match.group(2))
            if key in expected and expected[key] != value:
                raise RuntimeError('Inconsistent localization fallback: ' + key)
            expected[key] = value
    directory = root / 'GameData' / 'VolumetricExplosionFX' / 'Localization'
    if {p.name for p in directory.iterdir() if p.is_file()} != {lang + '.cfg' for lang in LANGUAGES}:
        raise RuntimeError('Expected the nine KSP localization catalogs')
    for language in LANGUAGES:
        content = (directory / (language + '.cfg')).read_text(encoding='utf-8')
        if not re.fullmatch(r'Localization\s*\{\s*' + re.escape(language) + r'\s*\{\s*(?:#VEFX_[^\n]+\n\s*)+\}\s*\}\s*', content):
            raise RuntimeError('Invalid localization node: ' + language)
        values = {}
        for line in content.splitlines():
            match = re.match(r'\s*#VEFX_(\w+)\s*=\s*(.*?)\s*$', line)
            if not match:
                continue
            key, value = match.groups()
            if key in values or not value or '//' in value or '{' in value or '}' in value:
                raise RuntimeError('Invalid localization entry: ' + language + '/' + key)
            values[key] = value
        if set(values) != set(expected):
            raise RuntimeError('Localization key coverage differs: ' + language)
        for key, value in values.items():
            normalized = re.sub(r'<<(\d+)>>', lambda m: '{' + str(int(m.group(1)) - 1) + '}', value)
            if sorted(re.findall(r'\{\d+\}', normalized)) != sorted(re.findall(r'\{\d+\}', expected[key])):
                raise RuntimeError('Localization arguments differ: ' + language + '/' + key)
            if language == 'en-us' and normalized != expected[key]:
                raise RuntimeError('English fallback differs: ' + key)
    return len(expected) * len(LANGUAGES)
