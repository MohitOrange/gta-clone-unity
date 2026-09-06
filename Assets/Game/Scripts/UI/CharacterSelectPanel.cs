using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The wardrobe: pick which outfit the character in the lobby -- and in the city -- wears.
    ///
    /// The grid is built from <see cref="SkinLibrary"/>, which reads the Threads shop's own
    /// stock, so an outfit's name, price and level gate are authored exactly once. Ownership
    /// is the shop's existing rule (<c>PlayerProgress.Owns</c>), which means an outfit the
    /// player has not bought shows here as locked and cannot be worn -- the wardrobe never
    /// becomes a way around the till.
    ///
    /// Cells are cloned from a hidden template at runtime rather than built at edit time,
    /// because the number of outfits is data, not layout. Same pattern as <see cref="ShopUI"/>.
    /// </summary>
    public class CharacterSelectPanel : UiPanel
    {
        [Header("Grid")]
        public RectTransform CellTemplate;
        public RectTransform CellContainer;
        [Tooltip("Cells per row. Rows are however many that takes.")]
        public int Columns = 3;
        [Tooltip("Gap between cells, in canvas reference units.")]
        public float Gap = 20f;
        [Tooltip("Cells never grow past this, so three outfits do not each become a poster.")]
        public Vector2 MaxCellSize = new Vector2(340f, 300f);

        [Header("Chrome")]
        public Text TitleLabel;
        public Text StatusLabel;
        public Button CloseButton;

        [Header("Links")]
        [Tooltip("Repainted as the selection changes, so the lobby shows what was just picked.")]
        public LobbyPreview Preview;

        [Header("Colours")]
        [Tooltip("All five come from UiTheme at build time -- the theme is an editor-only "
                 + "class, so it hands its palette over rather than being read at runtime.")]
        public Color OwnedTint = Color.white;
        public Color LockedTint = new Color(0.78f, 0.76f, 0.72f, 1f);
        public Color WearingText = new Color(0.28f, 0.52f, 0.24f, 1f);
        public Color OwnedText = new Color(0.24f, 0.19f, 0.15f, 0.62f);
        public Color LockedText = new Color(0.62f, 0.36f, 0.06f, 1f);

        readonly List<RectTransform> _cells = new List<RectTransform>();
        float _statusTimer;

        protected override void Awake()
        {
            base.Awake();

            if (CellTemplate != null) CellTemplate.gameObject.SetActive(false);
            if (CloseButton != null) CloseButton.onClick.AddListener(Hide);
        }

        protected override void OnShown()
        {
            MenuState.Enter();
            SetStatus("");
            Rebuild();
        }

        protected override void OnHidden()
        {
            MenuState.Exit();
            ClearCells();
        }

        protected override void Update()
        {
            base.Update();

            if (_statusTimer <= 0f) return;

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) SetStatus("");
        }

        // ------------------------------------------------------------------- grid

        void Rebuild()
        {
            ClearCells();

            if (CellTemplate == null || CellContainer == null) return;

            var progress = PlayerProgress.Instance;
            var skins = SkinLibrary.Skins;

            // Entry zero is the model as authored. It is not a shop item and cannot be locked,
            // so it is described here rather than looked up.
            int count = skins.Count + 1;
            int columns = Mathf.Max(1, Columns);
            int rows = Mathf.CeilToInt(count / (float)columns);

            Vector2 area = CellContainer.rect.size;
            float cellW = Mathf.Min(MaxCellSize.x, (area.x - (columns - 1) * Gap) / columns);
            float cellH = Mathf.Min(MaxCellSize.y, (area.y - (rows - 1) * Gap) / rows);

            // The block is centred in the container rather than pinned to its top-left, so a
            // part-filled last row does not leave the grid hanging off one edge.
            float blockW = columns * cellW + (columns - 1) * Gap;
            float blockH = rows * cellH + (rows - 1) * Gap;
            float originX = -blockW * 0.5f + cellW * 0.5f;
            float originY = blockH * 0.5f - cellH * 0.5f;

            AddCell(0, new Vector2(cellW, cellH), originX, originY, columns,
                    "", "Standard Issue", Color.white, true, "");

            for (int i = 0; i < skins.Count; i++)
            {
                var item = skins[i];
                bool owned = progress != null && progress.Owns(item.Id);

                string note = owned
                    ? ""
                    : progress != null && progress.Level < item.RequiredLevel
                        ? "LEVEL " + item.RequiredLevel
                        : "$" + item.Price.ToString("N0");

                AddCell(i + 1, new Vector2(cellW, cellH), originX, originY, columns,
                        item.Id, item.DisplayName, item.Colour, owned, note);
            }

            if (TitleLabel != null)
            {
                int owned = 1;
                foreach (var item in skins)
                    if (progress != null && progress.Owns(item.Id)) owned++;

                TitleLabel.text = "CHARACTER   " + owned + " / " + count;
            }
        }

        void AddCell(int index, Vector2 size, float originX, float originY, int columns,
                     string skinId, string displayName, Color colour, bool owned, string note)
        {
            var cell = Instantiate(CellTemplate, CellContainer);
            cell.gameObject.SetActive(true);
            _cells.Add(cell);

            int column = index % columns;
            int row = index / columns;

            cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 0.5f);
            cell.pivot = new Vector2(0.5f, 0.5f);
            cell.sizeDelta = size;
            cell.anchoredPosition = new Vector2(originX + column * (size.x + Gap),
                                                originY - row * (size.y + Gap));

            var progress = PlayerProgress.Instance;
            bool wearing = progress != null && progress.ActiveSkin == skinId;

            SetChildText(cell, "Name", displayName);
            SetChildText(cell, "Status", wearing ? "WEARING" : owned ? "OWNED" : note);

            var status = FindChild<Text>(cell, "Status");
            if (status != null)
                status.color = wearing ? WearingText : owned ? OwnedText : LockedText;

            var swatch = FindChild<Image>(cell, "Swatch");
            if (swatch != null) swatch.color = owned ? colour : Color.Lerp(colour, LockedTint, 0.65f);

            var lockIcon = FindChild<Image>(cell, "Lock");
            if (lockIcon != null) lockIcon.gameObject.SetActive(!owned);

            var frame = FindChild<Image>(cell, "Selected");
            if (frame != null) frame.gameObject.SetActive(wearing);

            var background = cell.GetComponent<Image>();
            if (background != null) background.color = owned ? OwnedTint : LockedTint;

            var button = cell.GetComponent<Button>();
            if (button == null) return;

            // Locked cells stay tappable on purpose: tapping one should say why it is locked,
            // not do nothing. A dead button reads as a broken button.
            button.onClick.RemoveAllListeners();
            string id = skinId;
            string name = displayName;
            bool unlocked = owned;
            string reason = note;
            button.onClick.AddListener(() => Choose(id, name, unlocked, reason));
        }

        void Choose(string skinId, string displayName, bool owned, string note)
        {
            if (!owned)
            {
                SetStatus(displayName + " -- " + note + " at Threads");
                return;
            }

            var progress = PlayerProgress.Instance;
            if (progress == null) return;

            progress.SetSkin(skinId);
            Preview?.ApplyActiveSkin();

            // The player's own body is repainted by PlayerSkinSwapper listening to SkinChanged,
            // which is the same path a purchase takes -- nothing here reaches into the player.
            var save = SaveSystem.Instance;
            if (save != null && save.SaveExists) save.Save();

            SetStatus("Now wearing " + displayName);
            Rebuild();
        }

        void SetStatus(string text)
        {
            if (StatusLabel != null) StatusLabel.text = text;
            _statusTimer = string.IsNullOrEmpty(text) ? 0f : 3f;
        }

        void ClearCells()
        {
            foreach (var cell in _cells)
                if (cell != null) Destroy(cell.gameObject);

            _cells.Clear();
        }

        static void SetChildText(RectTransform cell, string childName, string value)
        {
            var text = FindChild<Text>(cell, childName);
            if (text != null) text.text = value;
        }

        static T FindChild<T>(RectTransform cell, string childName) where T : Component
        {
            var child = cell.Find(childName);
            return child != null ? child.GetComponent<T>() : null;
        }
    }
}
