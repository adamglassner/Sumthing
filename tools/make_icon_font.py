"""Builds Sumthing's icon fonts from Microsoft's Fluent UI System Icons (MIT), the same way WillowWallet does.

Each icon sits at a fixed code point the app uses (mostly the familiar Segoe MDL2 codes).
Output: src/Sumthing/Assets/Fonts/SumthingIcons-{Regular,Filled}.ttf

    python tools/make_icon_font.py <path to fluentui-system-icons/fonts>
"""
import json, sys
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._c_m_a_p import cmap_format_4

ICONS = {  # code point used in the app -> Fluent icon name
    0xE700: 'navigation', 0xE706: 'weather_sunny', 0xE708: 'weather_moon', 0xE70D: 'chevron_down',
    0xE70E: 'chevron_up', 0xE70F: 'edit', 0xE710: 'add', 0xE711: 'dismiss', 0xE713: 'settings', 0xE718: 'pin',
    0xE738: 'subtract', 0xE73E: 'checkmark', 0xE740: 'full_screen_maximize', 0xE73F: 'full_screen_minimize',
    0xE74E: 'save', 0xE749: 'print', 0xE74D: 'delete', 0xE750: 'backspace', 0xE76B: 'chevron_left',
    0xE76C: 'chevron_right', 0xE81C: 'history', 0xE8A5: 'document_text', 0xE8AB: 'arrow_swap', 0xE8C8: 'copy',
    0xE8CB: 'arrow_sort', 0xE8EF: 'calculator', 0xE8A9: 'table', 0xE90F: 'wrench', 0xE9D2: 'data_trending',
    0xE9F9: 'data_histogram', 0xE71E: 'zoom_in', 0xE71F: 'zoom_out', 0xE7C9: 'target', 0xE946: 'info',
    0xE945: 'math_formula', 0xE9A6: 'data_line', 0xE92C: 'arrow_minimize', 0xE92D: 'arrow_maximize',
    0xE8F4: 'notepad', 0xE7B8: 'toolbox', 0xE72C: 'arrow_clockwise', 0xE9CE: 'question_circle',
    0xE9A9: 'scale_fit', 0xE9F8: 'data_scatter', 0xE777: 'arrow_reset', 0xE7C3: 'slide_settings',
    0xE8D4: 'panel_right', 0xE7B3: 'eye', 0xE7B4: 'eye_off', 0xE790: 'color', 0xE7AD: 'ruler',
    0xE7C2: 'arrow_move', 0xE8B0: 'cursor_click', 0xE94C: 'math_symbols', 0xE8F9: 'number_symbol',
}

def build(src_dir, style, family):
    names = json.load(open(f'{src_dir}/FluentSystemIcons-{style}.json'))
    font = TTFont(f'{src_dir}/FluentSystemIcons-{style}.ttf')
    cmap = font.getBestCmap()
    chosen = {}
    for cp, name in ICONS.items():
        for size in (20, 24, 16, 28):
            key = f'ic_fluent_{name}_{size}_{style.lower()}'
            if key in names:
                chosen[cp] = cmap[names[key]]
                break
        else:
            sys.exit(f'missing icon: {name} ({style})')
    opts = subset.Options()
    opts.glyph_names = True
    opts.notdef_outline = True
    opts.name_IDs = ['*']
    opts.layout_features = []
    sub = subset.Subsetter(opts)
    sub.populate(glyphs=sorted(set(chosen.values())))
    sub.subset(font)
    table = cmap_format_4(4)
    table.platformID, table.platEncID, table.language = 3, 1, 0
    table.cmap = chosen
    font['cmap'].tables = [table]
    for rec in font['name'].names:
        if rec.nameID in (1, 16, 4):
            rec.string = family
        elif rec.nameID == 6:
            rec.string = family.replace(' ', '')
        elif rec.nameID in (2, 17):
            rec.string = 'Regular'
    out = f'src/Sumthing/Assets/Fonts/SumthingIcons-{style}.ttf'
    font.save(out)
    print(out, len(chosen), 'icons')

src = sys.argv[1]
build(src, 'Regular', 'Sumthing Icons')
build(src, 'Filled', 'Sumthing Icons Filled')
