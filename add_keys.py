import codecs

file_path = r'DevelopmentTools.Addin\Core\LanguageManager.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# I will add the new keys to the default Dictionary
new_keys = '''
            { "TileElev_TitleBlock", new[] { "預設圖框", "Title Block", "図枠" } },
            { "TileElev_ViewportType", new[] { "視埠類型", "Viewport Type", "ビューポート" } },
            { "TileElev_SidePadding", new[] { "兩側預留(mm)", "Side Padding(mm)", "両側余白(mm)" } },
'''

content = content.replace('{ "TileElev_Tip",', new_keys.strip() + '\n            { "TileElev_Tip",')

with open(file_path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
