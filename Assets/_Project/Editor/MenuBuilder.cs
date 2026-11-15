using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SliceAndSell
{
    // Shop-sign main menu and end-of-shift results board (GDD > 3D user interface), built with UiKit.
    // Both hang over the far half of the counter, tilted back 20 degrees toward the player's eyes: readable by ray
    // from where they stand (~0.85 m), pokeable with a short lean, and clear of the customers' street (z 1.35).
    // Sizes in mm (1 canvas unit = 1 mm). Shop-sign buttons are >= 60 mm (GDD), gaps >= 20 mm.
    public static class MenuBuilder
    {
        const string StarPath = "Assets/_Project/Materials/UI/Star.png";
        const float ButtonText = 18f, TitleText = 36f, BodyText = 16f;
        static readonly Vector3 SignPos = new Vector3(0, 1.22f, 0.8f), BoardPos = new Vector3(0, 1.22f, 0.79f);
        static readonly Quaternion Tilt = Quaternion.Euler(20, 0, 0);

        public static (MainMenu menu, ResultsBoard results) Build(Transform stall, OrderManager orders)
        {
            var star = StarSprite();
            var menu = BuildMenu(stall, orders, star);
            var results = BuildResults(stall, orders, menu, star);
            UiKit.CheckSizes(menu.transform);
            UiKit.CheckSizes(results.transform);
            return (menu, results);
        }

        static MainMenu BuildMenu(Transform stall, OrderManager orders, Sprite star)
        {
            var menu = new GameObject("Main menu").AddComponent<MainMenu>();
            menu.transform.SetParent(stall, false);
            menu.orders = orders;
            var sign = UiKit.Panel("Shop sign", menu.transform, stall.TransformPoint(SignPos), stall.rotation * Tilt, new Vector2(600, 420), UiKit.Wood);
            menu.sign = sign.gameObject;

            // Main: Start (the next unfinished level) on top, then a 2 x 2 grid.
            var main = Page(sign, "Main", "Slice & Sell");
            var start = Btn(main, "Start", 0, 60, 400, 80, menu.StartGame, primary: true);
            menu.startLabel = start.GetComponentInChildren<TMP_Text>();
            menu.startLabel.fontSize = 22;
            Btn(main, "Level select", -105, -42, 190, 60, menu.ShowLevels);
            Btn(main, "How to play", 105, -42, 190, 60, menu.ShowHowTo);
            Btn(main, "Settings", -105, -122, 190, 60, menu.ShowSettings);
            Btn(main, "Quit", 105, -122, 190, 60, menu.Quit);

            // Level select: 2 columns of 250 x 80 buttons, title on top, best stars below (room for 4 levels).
            var levels = Page(sign, "Level select", "Level select");
            int n = orders.levels.Length;
            menu.levelButtons = new Button[n];
            menu.levelStars = new Image[n * 3];
            for (int i = 0; i < n; i++)
            {
                var b = Btn(levels, $"{i + 1}. {orders.levels[i].title}", i % 2 == 0 ? -135 : 135, 75 - i / 2 * 96, 250, 80, null);
                UnityEventTools.AddIntPersistentListener(b.onClick, menu.PlayLevel, i);
                b.GetComponentInChildren<TMP_Text>().rectTransform.anchorMin = new Vector2(0, 0.45f);
                Stars(b.transform.Find("Face"), star, new Vector2(0, -22), 18, 24).CopyTo(menu.levelStars, i * 3);
                menu.levelButtons[i] = b;
            }
            Back(levels, menu);

            // How to play: controllers and hands side by side.
            var howTo = Page(sign, "How to play", "How to play");
            UiKit.Label(howTo, "Controllers", new Vector2(-45, 112), new Vector2(210, 30), 20, UiKit.Accent, style: FontStyles.Bold);
            UiKit.Label(howTo, "Hands", new Vector2(185, 112), new Vector2(210, 30), 20, UiKit.Accent, style: FontStyles.Bold);
            string[,] rows =
            {
                { "Grab", "Grip button", "Pinch, or close your fist" },
                { "Cut", "Push the blade down along the line", "Push the blade down along the line" },
                { "Bell", "Poke it (or point + trigger)", "Poke it (or point + pinch)" },
                { "Cleaver", "Both grips on the handle, chop down", "Both hands on the handle, push down" },
                { "Menu", "Menu button", "Turn your left palm up" },
            };
            for (int r = 0; r < rows.GetLength(0); r++)
            {
                float y = 75 - r * 44;     // 5 rows above the Back button, every other one on a darker band
                if (r % 2 == 0) UiKit.Rounded(howTo, "Row band", new Vector2(0, y), new Vector2(560, 42), new Color(0f, 0f, 0f, 0.16f), 6f);
                UiKit.Label(howTo, rows[r, 0], new Vector2(-220, y), new Vector2(120, 44), BodyText, UiKit.Accent, style: FontStyles.Bold);
                UiKit.Label(howTo, rows[r, 1], new Vector2(-45, y), new Vector2(210, 44), BodyText, UiKit.Cream);
                UiKit.Label(howTo, rows[r, 2], new Vector2(185, y), new Vector2(210, 44), BodyText, UiKit.Cream);
            }
            Back(howTo, menu);

            // Settings: stance toggle, volume - / +.
            var settings = Page(sign, "Settings", "Settings");
            menu.stanceLabel = Btn(settings, "Stance: Standing", 0, 70, 360, 70, menu.ToggleStance).GetComponentInChildren<TMP_Text>();
            Btn(settings, "−", -150, -30, 70, 70, menu.VolumeDown).GetComponentInChildren<TMP_Text>().fontSize = 30;
            UiKit.Rounded(settings, "Volume pill", new Vector2(0, -30), new Vector2(200, 56), new Color(0f, 0f, 0f, 0.22f), 12f);
            menu.volumeLabel = UiKit.Label(settings, "Volume 100%", new Vector2(0, -30), new Vector2(200, 50), 22, UiKit.Cream, style: FontStyles.Bold);
            Btn(settings, "+", 150, -30, 70, 70, menu.VolumeUp).GetComponentInChildren<TMP_Text>().fontSize = 30;
            Back(settings, menu);

            menu.mainPage = main.gameObject; menu.levelsPage = levels.gameObject;
            menu.howToPage = howTo.gameObject; menu.settingsPage = settings.gameObject;
            foreach (var p in new[] { levels, howTo, settings }) p.gameObject.SetActive(false);
            return menu;
        }

        static ResultsBoard BuildResults(Transform stall, OrderManager orders, MainMenu menu, Sprite star)
        {
            var results = new GameObject("Results board").AddComponent<ResultsBoard>();
            results.transform.SetParent(stall, false);
            results.orders = orders;
            var board = UiKit.Panel("Results", results.transform, stall.TransformPoint(BoardPos), stall.rotation * Tilt, new Vector2(520, 360), UiKit.Wood);
            results.board = board.gameObject;

            results.heading = UiKit.Label(board, "Shift won!", new Vector2(0, 132), new Vector2(480, 50), TitleText, UiKit.Cream, style: FontStyles.Bold);
            UiKit.Rule(board, new Vector2(0, 104), 70);
            results.levelLabel = UiKit.Label(board, "Level 1", new Vector2(0, 82), new Vector2(480, 30), 18, UiKit.CreamDim);
            UiKit.Rounded(board, "Earnings pill", new Vector2(0, 40), new Vector2(400, 48), new Color(0f, 0f, 0f, 0.22f), 12f);
            results.earnings = UiKit.Label(board, "Earned ฿0    Target ฿0", new Vector2(0, 40), new Vector2(380, 40), 22, UiKit.Cream, style: FontStyles.Bold);
            results.stars = Stars(board, star, new Vector2(0, -22), 50, 65);

            Btn(board, "Retry", -160, -110, 140, 60, orders.Retry);
            results.nextButton = Btn(board, "Next level", 0, -110, 140, 60, orders.NextLevel, primary: true);
            var toMenu = Btn(board, "Menu", 160, -110, 140, 60, orders.StopShift);
            UnityEventTools.AddPersistentListener(toMenu.onClick, menu.Show);
            board.gameObject.SetActive(false);
            return results;
        }

        // A full-size page under the sign with its title; buttons on it are positioned from the sign's centre.
        static RectTransform Page(RectTransform sign, string name, string title)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(sign, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            UiKit.Label(rt, title, new Vector2(0, 165), new Vector2(560, 60), TitleText, UiKit.Cream, style: FontStyles.Bold);
            UiKit.Rule(rt, new Vector2(0, 136), 70);
            return rt;
        }

        static void Back(RectTransform page, MainMenu menu) => Btn(page, "Back", 0, -165, 160, 60, menu.ShowMain);

        // UiKit button with sign-sized text (read from ~0.85 m, not 0.5 m).
        static Button Btn(RectTransform parent, string label, float x, float y, float w, float h, UnityAction onClick, bool primary = false)
        {
            var b = UiKit.Button(parent, label, new Vector2(x, y), new Vector2(w, h), onClick, primary);
            b.GetComponentInChildren<TMP_Text>().fontSize = ButtonText;
            return b;
        }

        static Image[] Stars(Transform parent, Sprite sprite, Vector2 centre, float size, float spacing)
        {
            var stars = new Image[3];
            for (int s = 0; s < 3; s++)
            {
                var rt = (RectTransform)new GameObject("Star " + (s + 1), typeof(RectTransform), typeof(Image)).transform;
                rt.SetParent(parent, false);
                rt.anchoredPosition = centre + new Vector2((s - 1) * spacing, 0); rt.sizeDelta = Vector2.one * size;
                stars[s] = rt.GetComponent<Image>();
                stars[s].sprite = sprite; stars[s].color = MainMenu.StarOff; stars[s].raycastTarget = false;
            }
            return stars;
        }

        // The default TMP font has no star glyph, so stars are a generated 5-point sprite (4x supersampled edges, mipmapped).
        static Sprite StarSprite()
        {
            if (!File.Exists(StarPath))
            {
                const int n = 128;
                var pts = new Vector2[10];
                for (int k = 0; k < 10; k++)
                {
                    float a = Mathf.PI / 2 + k * Mathf.PI / 5, r = k % 2 == 0 ? 0.48f : 0.19f;
                    pts[k] = new Vector2(0.5f + r * Mathf.Cos(a), 0.5f + r * Mathf.Sin(a)) * n;
                }
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int hits = 0;
                        for (int s = 0; s < 4; s++) if (Inside(pts, new Vector2(x + 0.25f + 0.5f * (s % 2), y + 0.25f + 0.5f * (s / 2)))) hits++;
                        tex.SetPixel(x, y, new Color(1, 1, 1, hits / 4f));
                    }
                Directory.CreateDirectory(Path.GetDirectoryName(StarPath));
                File.WriteAllBytes(StarPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(StarPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(StarPath);
                imp.textureType = TextureImporterType.Sprite;
                imp.alphaIsTransparency = true; imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(StarPath);
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
