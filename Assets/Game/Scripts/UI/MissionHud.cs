using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Mission log, objective line, timer, reward banner and the money/level readout.
    ///
    /// Subscribes to the manager for one-shot events (start, end) but polls the objective and
    /// timer, because those change continuously and a missed event would strand the panel
    /// showing a stale objective.
    /// </summary>
    public class MissionHud : MonoBehaviour
    {
        static MissionHud _instance;

        public static MissionHud Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<MissionHud>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Objective panel")]
        public CanvasGroup ObjectivePanel;
        public Text MissionTitle;
        public Text ObjectiveLine;
        public Text TimerLine;
        public Text DistanceLine;

        [Header("Result banner")]
        public CanvasGroup ResultBanner;
        public Text ResultTitle;
        public Text ResultDetail;
        public Color SuccessColour = new Color(0.42f, 0.86f, 0.45f);
        public Color FailureColour = new Color(0.95f, 0.35f, 0.3f);
        public float ResultHold = 4f;

        [Header("Toast")]
        public CanvasGroup ToastGroup;
        public Text ToastLabel;
        public float ToastHold = 2.2f;

        [Header("Progress readout")]
        public Text MoneyLabel;
        public Text LevelLabel;
        public Image XpFill;

        [Header("Timer colours")]
        public Color TimerNormal = new Color(1f, 1f, 1f, 0.9f);
        public Color TimerUrgent = new Color(0.98f, 0.4f, 0.3f);
        [Tooltip("Seconds remaining at which the clock turns urgent.")]
        public float UrgentThreshold = 15f;

        [Header("Refs")]
        public HudContext Hud;

        MissionManager _manager;
        PlayerProgress _progress;
        Transform _player;

        float _resultTimer;
        float _toastTimer;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.MissionStarted -= OnMissionStarted;
                _manager.MissionEnded -= OnMissionEnded;
            }
            if (_progress != null)
            {
                _progress.MoneyChanged -= OnMoneyChanged;
                _progress.XpChanged -= OnXpChanged;
                _progress.LeveledUp -= OnLeveledUp;
            }
            if (_instance == this) _instance = null;
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            _manager = MissionManager.Instance;
            if (_manager != null)
            {
                _manager.MissionStarted += OnMissionStarted;
                _manager.MissionEnded += OnMissionEnded;
            }

            _progress = PlayerProgress.Instance;
            if (_progress != null)
            {
                _progress.MoneyChanged += OnMoneyChanged;
                _progress.XpChanged += OnXpChanged;
                _progress.LeveledUp += OnLeveledUp;

                OnMoneyChanged(_progress.Money);
                OnXpChanged(_progress.Xp, _progress.XpToNextLevel);
            }

            SetAlpha(ObjectivePanel, 0f);
            SetAlpha(ResultBanner, 0f);
            SetAlpha(ToastGroup, 0f);
        }

        void Update()
        {
            _manager ??= MissionManager.Instance;

            UpdateObjectivePanel();
            UpdateBanners();
        }

        void UpdateObjectivePanel()
        {
            var mission = _manager != null ? _manager.Active : null;
            bool showing = mission != null && mission.State == MissionState.Running;

            FadeTo(ObjectivePanel, showing ? 1f : 0f);
            if (!showing) return;

            if (MissionTitle != null) MissionTitle.text = mission.Title.ToUpperInvariant();
            if (ObjectiveLine != null) ObjectiveLine.text = mission.ObjectiveText;

            if (TimerLine != null)
            {
                if (mission.IsTimed)
                {
                    float t = Mathf.Max(0f, mission.TimeRemaining);
                    TimerLine.text = Mathf.FloorToInt(t / 60f) + ":" + (Mathf.FloorToInt(t) % 60).ToString("00");
                    TimerLine.color = t <= UrgentThreshold ? TimerUrgent : TimerNormal;
                    TimerLine.enabled = true;
                }
                else TimerLine.enabled = false;
            }

            if (DistanceLine != null)
            {
                if (mission.HasObjectivePoint && _player != null)
                {
                    Vector3 d = mission.ObjectivePoint - _player.position;
                    d.y = 0f;
                    DistanceLine.text = Mathf.RoundToInt(d.magnitude) + " m";
                    DistanceLine.enabled = true;
                }
                else DistanceLine.enabled = false;
            }
        }

        void UpdateBanners()
        {
            if (_resultTimer > 0f)
            {
                _resultTimer -= Time.deltaTime;
                if (_resultTimer <= 0f) FadeTo(ResultBanner, 0f);
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                if (_toastTimer <= 0f) FadeTo(ToastGroup, 0f);
            }

            // Fade both toward whatever their timer says, every frame.
            FadeTo(ResultBanner, _resultTimer > 0f ? 1f : 0f);
            FadeTo(ToastGroup, _toastTimer > 0f ? 1f : 0f);
        }

        // ------------------------------------------------------------------ events

        void OnMissionStarted(MissionBase mission)
        {
            ShowToast(mission.Title + " accepted");
        }

        void OnMissionEnded(MissionBase mission, bool success, string headline, string detail)
        {
            if (ResultTitle != null)
            {
                ResultTitle.text = headline;
                ResultTitle.color = success ? SuccessColour : FailureColour;
            }
            if (ResultDetail != null) ResultDetail.text = detail;

            _resultTimer = ResultHold;
        }

        void OnMoneyChanged(int money)
        {
            if (MoneyLabel != null) MoneyLabel.text = "$" + money.ToString("N0");
        }

        void OnXpChanged(int xp, int toNext)
        {
            if (_progress == null) return;

            if (LevelLabel != null) LevelLabel.text = "LV " + _progress.Level;
            if (XpFill != null) XpFill.fillAmount = _progress.LevelProgress;
        }

        void OnLeveledUp(int level, string unlock)
        {
            string message = "Level " + level;
            if (!string.IsNullOrEmpty(unlock)) message += " - unlocked " + PrettyUnlock(unlock);
            ShowToast(message);
        }

        static string PrettyUnlock(string id) => id switch
        {
            Unlocks.Bike => "the motorbike",
            Unlocks.Boat => "the speedboat",
            Unlocks.ExtendedAmmo => "extended ammo",
            Unlocks.BodyArmor => "body armour",
            Unlocks.NorthDistrict => "the north district",
            _ => id,
        };

        /// <summary>Brief message in the middle of the screen.</summary>
        public void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (ToastLabel != null) ToastLabel.text = message;
            _toastTimer = ToastHold;
        }

        // ------------------------------------------------------------------ fading

        static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group == null) return;
            group.alpha = alpha;
            group.blocksRaycasts = false;
        }

        static void FadeTo(CanvasGroup group, float target)
        {
            if (group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, target, 6f * Time.unscaledDeltaTime);
            group.blocksRaycasts = false;
        }
    }
}
