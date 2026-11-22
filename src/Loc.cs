using System;
using System.Collections.Generic;

namespace WallStretch
{
    /// <summary>Tiny two-language string table (English / Russian).</summary>
    public static class Loc
    {
        public const int English = 0;
        public const int Russian = 1;

        private static int _lang = English;
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>();

        static Loc()
        {
            Add("app.subtitle", "multi-monitor wallpapers", "панорама на несколько мониторов");

            Add("btn.paste", "Paste", "Вставить");
            Add("btn.open", "Open…", "Открыть…");
            Add("btn.fill", "Fill", "Заполнить");
            Add("btn.width", "Fit width", "По ширине");
            Add("btn.one", "1:1", "1:1");
            Add("btn.export", "Export…", "Экспорт…");
            Add("btn.monitors", "Screens", "Экраны");
            Add("btn.apply", "Apply wallpaper", "Применить обои");

            Add("tip.zoom", "Image scale. Click to reset to 1:1", "Масштаб картинки. Клик — вернуть 1:1");
            Add("tip.panorama", "Click a screen to leave it out of the panorama or bring it back",
                "Нажмите на экран, чтобы исключить его из панорамы или вернуть обратно");
            Add("tip.chip.on", "Click to leave this screen out of the panorama", "Нажмите, чтобы исключить экран");
            Add("tip.chip.off", "Click to bring this screen back", "Нажмите, чтобы вернуть экран");
            Add("tip.grip", "Drag to move the screen up or down · double-click to reset",
                "Перетащите, чтобы сдвинуть экран вверх-вниз · двойной клик — сброс");
            Add("tip.lang", "Interface language", "Язык интерфейса");

            Add("strip.title", "Panorama on:", "Панорама на:");

            Add("status.screens", "Screens: {0} of {1}", "Экранов: {0} из {1}");
            Add("status.area", "area {0}×{1}", "область {0}×{1}");
            Add("status.image", "image {0}×{1}", "картинка {0}×{1}");
            Add("status.hint", "paste a picture (Ctrl+V) or drag a file",
                "вставьте картинку (Ctrl+V) или перетащите файл");
            Add("status.excluded", "left out: {0}", "вне панорамы: {0}");
            Add("status.applying", "Building and applying the wallpaper…", "Собираю и применяю обои…");
            Add("status.applied", "Done: wallpaper applied to {0} screen(s)", "Готово: обои применены на {0} экран(а)");
            Add("status.saved", "Saved to {0}", "Сохранено в {0}");

            Add("hint.empty", "Drag an image here\r\nor press Ctrl+V / “Open”",
                "Перетащите изображение сюда\r\nили нажмите Ctrl+V / «Открыть»");

            Add("msg.noClipboard", "There is no image in the clipboard.", "В буфере обмена нет картинки.");
            Add("msg.needImage", "Paste or open an image first.", "Сначала вставьте или откройте изображение.");
            Add("msg.applyFailed", "Could not apply the wallpaper.\n\n{0}", "Не удалось применить обои.\n\n{0}");
            Add("msg.busy", "Windows was busy with a synchronous call. Press “Apply wallpaper” once more.",
                "Windows был занят синхронной операцией. Нажмите «Применить обои» ещё раз.");
            Add("msg.error", "WallStretch", "WallStretch");

            Add("dlg.open.title", "Choose an image for the wallpaper", "Выберите изображение для обоев");
            Add("dlg.open.filter", "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*",
                "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*");
            Add("dlg.export.title", "Where to save the per-monitor pictures",
                "Куда сохранить картинки для каждого монитора");
        }

        public static int Lang
        {
            get { return _lang; }
        }

        public static void Set(int lang)
        {
            _lang = lang == Russian ? Russian : English;
        }

        public static string T(string key)
        {
            string[] value;
            if (Table.TryGetValue(key, out value)) return value[_lang];
            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        private static void Add(string key, string en, string ru)
        {
            Table[key] = new string[] { en, ru };
        }
    }
}
