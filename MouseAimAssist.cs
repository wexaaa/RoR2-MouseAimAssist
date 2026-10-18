using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace MouseAimAssist
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions")]
    [DefaultExecutionOrder(10000)]
    public sealed class MouseAimAssistPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.wexaa.mouseaimassist";
        public const string PluginName = "Mouse Aim Assist";
        public const string PluginVersion = "1.2.0";

        private static MouseAimAssistPlugin Instance;
        private static bool _replicatingBullet;
        private static bool _replicatingProjectile;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _activationKey;
        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private ConfigEntry<bool> _silentAim;
        private ConfigEntry<bool> _allEnemiesMode;
        private ConfigEntry<float> _strength;
        private ConfigEntry<float> _smoothing;
        private ConfigEntry<float> _fieldOfView;
        private ConfigEntry<float> _maximumDistance;
        private ConfigEntry<bool> _requireLineOfSight;
        private ConfigEntry<float> _verticalOffset;
        private ConfigEntry<bool> _moveCamera;
        private ConfigEntry<bool> _showIndicator;

        private Vector3 _assistedDirection;
        private bool _hasAssistedDirection;
        private bool _aimToggled = true;
        private bool _hasTarget;
        private Texture2D _indicatorTexture;
        private GUIStyle _indicatorStyle;
        private Harmony _harmony;
        private readonly List<HurtBox> _mapTargets = new List<HurtBox>();
        private readonly HashSet<HealthComponent> _seenHealthComponents = new HashSet<HealthComponent>();

        private void Awake()
        {
            Instance = this;
            BindConfiguration();
            RegisterOptions();
            CreateIndicatorTexture();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(MouseAimAssistPlugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void BindConfiguration()
        {
            _enabled = Config.Bind(
                "01. Основное",
                "Включено",
                true,
                "Включает помощь в прицеливании.");

            _activationKey = Config.Bind(
                "01. Основное",
                "Клавиша активации",
                new KeyboardShortcut(KeyCode.None),
                "Необязательная клавиша удержания. None означает постоянную работу, пока мод включён.");

            _toggleKey = Config.Bind(
                "01. Основное",
                "Переключатель",
                new KeyboardShortcut(KeyCode.F6),
                "Включает или выключает прицеливание во время игры.");

            _silentAim = Config.Bind(
                "01. Основное",
                "Сайлент-аим",
                true,
                "Мгновенно направляет стандартный луч атаки в цель, не поворачивая камеру.");

            _allEnemiesMode = Config.Bind(
                "01. Основное",
                "Все враги на карте",
                true,
                "Каждая обычная пуля или выпущенный снаряд дублируется по всем живым противникам на карте.");

            _strength = Config.Bind(
                "02. Прицеливание",
                "Сила срабатывания",
                35f,
                new ConfigDescription(
                    "Насколько далеко прицел доводится к цели: 0% — выключено, 100% — полное наведение.",
                    new AcceptableValueRange<float>(0f, 100f)));

            _smoothing = Config.Bind(
                "02. Прицеливание",
                "Плавность",
                10f,
                new ConfigDescription(
                    "Скорость доведения. Меньше — мягче и медленнее, больше — быстрее.",
                    new AcceptableValueRange<float>(1f, 30f)));

            _fieldOfView = Config.Bind(
                "02. Прицеливание",
                "Радиус захвата",
                14f,
                new ConfigDescription(
                    "Максимальный угол от центра прицела, в котором ищется цель.",
                    new AcceptableValueRange<float>(1f, 60f)));

            _maximumDistance = Config.Bind(
                "02. Прицеливание",
                "Дальность",
                120f,
                new ConfigDescription(
                    "Максимальная дальность захвата цели в игровых метрах.",
                    new AcceptableValueRange<float>(10f, 500f)));

            _requireLineOfSight = Config.Bind(
                "03. Фильтры",
                "Только видимые цели",
                true,
                "Не наводиться на противников за стенами.");

            _verticalOffset = Config.Bind(
                "03. Фильтры",
                "Смещение точки прицеливания",
                0f,
                new ConfigDescription(
                    "Вертикальное смещение точки прицеливания относительно выбранного хитбокса.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            _moveCamera = Config.Bind(
                "03. Фильтры",
                "Двигать камеру",
                false,
                "Показывать мягкое доведение визуально. В режиме сайлент-аима камера всегда неподвижна.");

            _showIndicator = Config.Bind(
                "04. Интерфейс",
                "Показывать индикатор",
                true,
                "Показывать состояние сайлент-аима в правом верхнем углу.");
        }

        private void RegisterOptions()
        {
            ModSettingsManager.SetModDescription(
                "Сайлент-аим и мягкая помощь в прицеливании для мыши. F6 включает и выключает мод.");

            ModSettingsManager.AddOption(new CheckBoxOption(_enabled));
            ModSettingsManager.AddOption(new KeyBindOption(_activationKey));
            ModSettingsManager.AddOption(new KeyBindOption(_toggleKey));
            ModSettingsManager.AddOption(new CheckBoxOption(_silentAim));
            ModSettingsManager.AddOption(new CheckBoxOption(_allEnemiesMode));
            ModSettingsManager.AddOption(new SliderOption(_strength, Slider(0f, 100f, "{0:0}%")));
            ModSettingsManager.AddOption(new SliderOption(_smoothing, Slider(1f, 30f, "{0:0.0}")));
            ModSettingsManager.AddOption(new SliderOption(_fieldOfView, Slider(1f, 60f, "{0:0.0}°")));
            ModSettingsManager.AddOption(new SliderOption(_maximumDistance, Slider(10f, 500f, "{0:0} м")));
            ModSettingsManager.AddOption(new CheckBoxOption(_requireLineOfSight));
            ModSettingsManager.AddOption(new SliderOption(_verticalOffset, Slider(-2f, 2f, "{0:0.00} м")));
            ModSettingsManager.AddOption(new CheckBoxOption(_moveCamera));
            ModSettingsManager.AddOption(new CheckBoxOption(_showIndicator));
        }

        private static SliderConfig Slider(float minimum, float maximum, string format)
        {
            SliderConfig config = new SliderConfig();
            config.min = minimum;
            config.max = maximum;
            config.FormatString = format;
            return config;
        }

        private void Update()
        {
            if (_toggleKey.Value.IsDown())
            {
                _aimToggled = !_aimToggled;
                _hasAssistedDirection = false;
                _hasTarget = false;
            }
        }

        private void FixedUpdate()
        {
            ApplyAim(false);
        }

        private void LateUpdate()
        {
            ApplyAim(true);
        }

        private void ApplyAim(bool allowCameraMovement)
        {
            if (!_enabled.Value || !_aimToggled || !ActivationKeyIsHeld())
            {
                _hasAssistedDirection = false;
                _hasTarget = false;
                return;
            }

            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser == null || localUser.isUIFocused)
            {
                _hasAssistedDirection = false;
                _hasTarget = false;
                return;
            }

            CharacterBody body = localUser.cachedBody;
            CameraRigController cameraRig = localUser.cameraRigController;
            if (body == null || body.inputBank == null || body.teamComponent == null || cameraRig == null || cameraRig.sceneCam == null)
            {
                _hasAssistedDirection = false;
                _hasTarget = false;
                return;
            }

            if (_allEnemiesMode.Value)
            {
                _hasTarget = HasAnyEnemy(body);
                _hasAssistedDirection = false;
                return;
            }

            Vector3 rawDirection = cameraRig.sceneCam.transform.forward.normalized;
            Vector3 aimOrigin = body.inputBank.aimOrigin;
            HurtBox target = FindTarget(body, aimOrigin, rawDirection);
            _hasTarget = target != null;
            if (target == null)
            {
                _hasAssistedDirection = false;
                return;
            }

            Vector3 targetPoint = target.transform.position + Vector3.up * _verticalOffset.Value;
            Vector3 targetDirection = targetPoint - aimOrigin;
            if (targetDirection.sqrMagnitude < 0.0001f)
                return;

            targetDirection.Normalize();

            if (_silentAim.Value)
            {
                _assistedDirection = targetDirection;
                _hasAssistedDirection = true;
                body.inputBank.aimDirection = targetDirection;
                return;
            }

            if (_strength.Value <= 0f)
            {
                _hasAssistedDirection = false;
                return;
            }

            float strength01 = Mathf.Clamp01(_strength.Value / 100f);
            Vector3 weightedDirection = Vector3.Slerp(rawDirection, targetDirection, strength01).normalized;

            if (!_hasAssistedDirection)
            {
                _assistedDirection = rawDirection;
                _hasAssistedDirection = true;
            }

            float response = Mathf.Max(0.01f, _smoothing.Value);
            float blend = 1f - Mathf.Exp(-response * Time.unscaledDeltaTime);
            _assistedDirection = Vector3.Slerp(_assistedDirection, weightedDirection, blend).normalized;

            body.inputBank.aimDirection = _assistedDirection;

            if (allowCameraMovement && _moveCamera.Value)
                cameraRig.sceneCam.transform.rotation = Quaternion.LookRotation(_assistedDirection, Vector3.up);
        }

        private void CreateIndicatorTexture()
        {
            _indicatorTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            _indicatorTexture.name = "MouseAimAssistIndicator";
            _indicatorTexture.hideFlags = HideFlags.HideAndDontSave;

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 7.5f;
                    float dy = y - 7.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = distance >= 4.5f && distance <= 6.5f;
                    bool cross = (x == 7 || x == 8 || y == 7 || y == 8) && distance <= 7.5f;
                    _indicatorTexture.SetPixel(x, y, ring || cross ? Color.white : Color.clear);
                }
            }

            _indicatorTexture.Apply(false, true);
        }

        private void OnGUI()
        {
            if (!_showIndicator.Value || _indicatorTexture == null)
                return;

            bool active = _enabled.Value && _aimToggled && ActivationKeyIsHeld();
            Color statusColor = active ? (_hasTarget ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.8f, 0.2f)) : new Color(1f, 0.25f, 0.25f);
            string statusText = active ? (_hasTarget ? "SILENT AIM: LOCK" : "SILENT AIM: ON") : "SILENT AIM: OFF";

            if (_indicatorStyle == null)
            {
                _indicatorStyle = new GUIStyle(GUI.skin.label);
                _indicatorStyle.fontSize = 15;
                _indicatorStyle.fontStyle = FontStyle.Bold;
                _indicatorStyle.alignment = TextAnchor.MiddleLeft;
            }

            float left = Screen.width - 190f;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(left - 8f, 12f, 182f, 34f), Texture2D.whiteTexture);
            GUI.color = statusColor;
            GUI.DrawTexture(new Rect(left, 21f, 16f, 16f), _indicatorTexture);
            _indicatorStyle.normal.textColor = statusColor;
            GUI.Label(new Rect(left + 24f, 16f, 150f, 26f), statusText, _indicatorStyle);
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();

            if (_indicatorTexture != null)
                Destroy(_indicatorTexture);

            if (Instance == this)
                Instance = null;
        }

        private bool IsMapAimActive(GameObject owner)
        {
            if (!_allEnemiesMode.Value || !_enabled.Value || !_aimToggled || !ActivationKeyIsHeld() || owner == null)
                return false;

            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            CharacterBody localBody = localUser != null ? localUser.cachedBody : null;
            if (localBody == null)
                return false;

            CharacterBody ownerBody = owner.GetComponent<CharacterBody>();
            return owner == localBody.gameObject || ownerBody == localBody;
        }

        private bool HasAnyEnemy(CharacterBody body)
        {
            TeamMask enemies = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);
            IList<HurtBox> hurtBoxes = HurtBox.readOnlyBullseyesList;
            for (int i = 0; i < hurtBoxes.Count; i++)
            {
                HurtBox hurtBox = hurtBoxes[i];
                if (hurtBox != null && hurtBox.healthComponent != null && hurtBox.healthComponent.alive && enemies.HasTeam(hurtBox.teamIndex))
                    return true;
            }

            return false;
        }

        private List<HurtBox> GetAllMapTargets(GameObject owner)
        {
            _mapTargets.Clear();
            _seenHealthComponents.Clear();

            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            CharacterBody body = localUser != null ? localUser.cachedBody : null;
            if (body == null || body.teamComponent == null)
                return _mapTargets;

            TeamMask enemies = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);
            IList<HurtBox> hurtBoxes = HurtBox.readOnlyBullseyesList;
            for (int i = 0; i < hurtBoxes.Count; i++)
            {
                HurtBox hurtBox = hurtBoxes[i];
                if (hurtBox == null || hurtBox.healthComponent == null || !hurtBox.healthComponent.alive)
                    continue;
                if (!enemies.HasTeam(hurtBox.teamIndex) || !_seenHealthComponents.Add(hurtBox.healthComponent))
                    continue;

                _mapTargets.Add(hurtBox);
            }

            _hasTarget = _mapTargets.Count > 0;
            return _mapTargets;
        }

        [HarmonyPatch(typeof(BulletAttack), "Fire", new Type[0])]
        private static class BulletAttackFirePatch
        {
            private static bool Prefix(BulletAttack __instance)
            {
                if (_replicatingBullet || Instance == null || !Instance.IsMapAimActive(__instance.owner))
                    return true;

                List<HurtBox> targets = Instance.GetAllMapTargets(__instance.owner);
                if (targets.Count == 0)
                    return true;

                Vector3 originalAim = __instance.aimVector;
                float originalMaxDistance = __instance.maxDistance;
                float originalMinSpread = __instance.minSpread;
                float originalMaxSpread = __instance.maxSpread;
                LayerMask originalStopperMask = __instance.stopperMask;
                bool originalTrajectoryAssist = __instance.allowTrajectoryAimAssist;

                _replicatingBullet = true;
                try
                {
                    __instance.minSpread = 0f;
                    __instance.maxSpread = 0f;
                    __instance.stopperMask = (LayerMask)0;
                    __instance.allowTrajectoryAimAssist = false;

                    for (int i = 0; i < targets.Count; i++)
                    {
                        HurtBox target = targets[i];
                        Vector3 direction = target.transform.position - __instance.origin;
                        float distance = direction.magnitude;
                        if (distance <= 0.001f)
                            continue;

                        __instance.aimVector = direction / distance;
                        __instance.maxDistance = Mathf.Max(originalMaxDistance, distance + 5f);
                        __instance.Fire();
                    }
                }
                finally
                {
                    __instance.aimVector = originalAim;
                    __instance.maxDistance = originalMaxDistance;
                    __instance.minSpread = originalMinSpread;
                    __instance.maxSpread = originalMaxSpread;
                    __instance.stopperMask = originalStopperMask;
                    __instance.allowTrajectoryAimAssist = originalTrajectoryAssist;
                    _replicatingBullet = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(ProjectileManager), "FireProjectile", new Type[] { typeof(FireProjectileInfo) })]
        private static class ProjectileFirePatch
        {
            private static bool Prefix(ProjectileManager __instance, ref FireProjectileInfo fireProjectileInfo)
            {
                if (_replicatingProjectile || Instance == null || !Instance.IsMapAimActive(fireProjectileInfo.owner))
                    return true;

                List<HurtBox> targets = Instance.GetAllMapTargets(fireProjectileInfo.owner);
                if (targets.Count == 0)
                    return true;

                _replicatingProjectile = true;
                try
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        HurtBox target = targets[i];
                        Vector3 direction = target.transform.position - fireProjectileInfo.position;
                        if (direction.sqrMagnitude <= 0.0001f)
                            continue;

                        FireProjectileInfo copy = fireProjectileInfo;
                        Vector3 normalizedDirection = direction.normalized;
                        copy.position = target.transform.position - normalizedDirection * 2f;
                        copy.rotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
                        copy.target = target.healthComponent.gameObject;
                        copy.maxDistance = 6f;
                        copy.speedOverride = 200f;
                        __instance.FireProjectile(copy);
                    }
                }
                finally
                {
                    _replicatingProjectile = false;
                }

                return false;
            }
        }

        private bool ActivationKeyIsHeld()
        {
            KeyboardShortcut shortcut = _activationKey.Value;
            return shortcut.MainKey == KeyCode.None || shortcut.IsPressed();
        }

        private HurtBox FindTarget(CharacterBody body, Vector3 origin, Vector3 direction)
        {
            BullseyeSearch search = new BullseyeSearch();
            search.viewer = body;
            search.searchOrigin = origin;
            search.searchDirection = direction;
            search.minDistanceFilter = 0f;
            search.maxDistanceFilter = _maximumDistance.Value;
            search.maxAngleFilter = _fieldOfView.Value;
            search.teamMaskFilter = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);
            search.filterByLoS = _requireLineOfSight.Value;
            search.filterByDistinctEntity = true;
            search.sortMode = BullseyeSearch.SortMode.Angle;
            search.RefreshCandidates();

            foreach (HurtBox hurtBox in search.GetResults())
            {
                if (hurtBox != null && hurtBox.healthComponent != null && hurtBox.healthComponent.alive)
                    return hurtBox;
            }

            return null;
        }
    }
}
