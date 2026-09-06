using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Applies bought upgrades to a spawned vehicle: engine, armour and paint.
    ///
    /// Multipliers are applied to the *stock* values captured on Awake rather than compounding
    /// on whatever is currently set. Without that, re-applying upgrades (on respawn, on load,
    /// on fitting a second part) would stack multiplicatively and produce a car that does
    /// 400 kph after three visits to the garage.
    /// </summary>
    public class VehicleUpgrades : MonoBehaviour
    {
        [Header("Fitted levels")]
        [Range(0, 3)] public int SpeedLevel;
        [Range(0, 3)] public int ArmorLevel;

        [Header("Paint")]
        public bool HasCustomColour;
        public Color Colour = Color.white;

        [Header("Steps")]
        [Tooltip("Top speed and torque multiplier per engine level.")]
        public float SpeedStep = 0.14f;
        [Tooltip("Max health multiplier per armour level.")]
        public float ArmorStep = 0.35f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Vehicle _vehicle;
        CarController _car;
        BikeController _bike;
        BoatController _boat;

        // Stock values, captured once so upgrades never compound.
        float _stockHealth;
        float _stockTopSpeed;
        float _stockTorque;
        bool _captured;

        void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            _car = GetComponent<CarController>();
            _bike = GetComponent<BikeController>();
            _boat = GetComponent<BoatController>();

            CaptureStock();
        }

        void Start() => Apply();

        void CaptureStock()
        {
            if (_captured || _vehicle == null) return;

            _stockHealth = _vehicle.MaxHealth;

            if (_car != null) { _stockTopSpeed = _car.TopSpeedKph; _stockTorque = _car.MotorTorque; }
            else if (_bike != null) { _stockTopSpeed = _bike.TopSpeedKph; _stockTorque = _bike.MotorTorque; }
            else if (_boat != null) { _stockTopSpeed = _boat.TopSpeedKph; _stockTorque = _boat.ThrustForce; }

            _captured = true;
        }

        /// <summary>Re-derive every tuned value from stock. Safe to call repeatedly.</summary>
        public void Apply()
        {
            CaptureStock();
            if (_vehicle == null) return;

            float speedMultiplier = 1f + SpeedLevel * SpeedStep;
            float healthMultiplier = 1f + ArmorLevel * ArmorStep;

            _vehicle.MaxHealth = _stockHealth * healthMultiplier;

            if (_car != null)
            {
                _car.TopSpeedKph = _stockTopSpeed * speedMultiplier;
                _car.MotorTorque = _stockTorque * speedMultiplier;
            }
            else if (_bike != null)
            {
                _bike.TopSpeedKph = _stockTopSpeed * speedMultiplier;
                _bike.MotorTorque = _stockTorque * speedMultiplier;
            }
            else if (_boat != null)
            {
                _boat.TopSpeedKph = _stockTopSpeed * speedMultiplier;
                _boat.ThrustForce = _stockTorque * speedMultiplier;
            }

            ApplyPaint();
        }

        void ApplyPaint()
        {
            if (!HasCustomColour) return;

            var damage = GetComponent<VehicleDamage>();
            var targets = damage != null && damage.BodyRenderers.Length > 0
                ? damage.BodyRenderers
                : GetComponentsInChildren<Renderer>();

            var mpb = new MaterialPropertyBlock();
            foreach (var r in targets)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Colour);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>Write this vehicle's fitted parts into save data.</summary>
        public void WriteTo(OwnedVehicleData data)
        {
            data.SpeedLevel = SpeedLevel;
            data.ArmorLevel = ArmorLevel;
            data.HasCustomColour = HasCustomColour;
            data.ColourR = Colour.r;
            data.ColourG = Colour.g;
            data.ColourB = Colour.b;
        }

        /// <summary>Restore fitted parts from save data and apply them.</summary>
        public void ReadFrom(OwnedVehicleData data)
        {
            if (data == null) return;

            SpeedLevel = data.SpeedLevel;
            ArmorLevel = data.ArmorLevel;
            HasCustomColour = data.HasCustomColour;
            Colour = new Color(data.ColourR, data.ColourG, data.ColourB);

            Apply();
        }

        /// <summary>Human-readable summary for the garage UI.</summary>
        public string Describe()
        {
            if (SpeedLevel == 0 && ArmorLevel == 0 && !HasCustomColour) return "Stock";
            return "Engine " + SpeedLevel + "   Armour " + ArmorLevel
                   + (HasCustomColour ? "   Custom paint" : "");
        }
    }
}
