using System;
using System.Collections;
using System.Collections.Generic;
using POTCO.Inventory;
using Player;
using UnityEngine;

namespace POTCO.Combat
{
    [DisallowMultipleComponent]
    public sealed class PotcoWeaponController : MonoBehaviour
    {
        [SerializeField] private bool createHud = true;
        [SerializeField] private bool allowMousePrimarySkill = true;
        [SerializeField] private bool allowKeyboardSkillNumbers = true;
        [SerializeField] private float comboResetSeconds = 1.25f;
        [SerializeField] private LayerMask weaponTargetMask = ~0;
        [SerializeField] private float maxAutoTargetAngle = 65f;

        private const float WeaponPowerMultiplier = 0.005f;
        private const int ItemTypeGun = 2;
        private const int ItemSubtypePistol = 6;
        private const int ItemSubtypeRepeater = 7;
        private const int ItemSubtypeMusket = 8;
        private const int ItemSubtypeBlunderbuss = 9;
        private const int ItemSubtypeBayonet = 10;
        private const int MusketTakeAimSkill = 12301;
        private const int BayonetShootSkill = 13100;
        private const int BayonetStabSkill = 13101;
        private const int BayonetRushSkill = 13102;
        private const int BayonetBashSkill = 13103;
        private const float GunShortRangeMultiplier = 0.4f;
        private const float GunMediumRangeMultiplier = 1f;
        private const float GunLongRangeMultiplier = 1.5f;
        private const float GunDeadzoneRange = 15f;
        private const float WeaponSwitchPutAwaySeconds = 0.25f;
        private const float ReferenceClickAttackRecoverySeconds = 0.75f;
        private readonly Dictionary<int, float> nextSkillUseTime = new Dictionary<int, float>();
        private readonly List<string> messages = new List<string>();
        private readonly List<string> weaponAnimationHistory = new List<string>();

        private PotcoInventoryController inventoryController;
        private SimpleAnimationPlayer animationPlayer;
        private PotcoWeaponCatalog catalog;
        private PotcoWeaponVisualResolver visualResolver;
        private PotcoWeaponDefinition currentWeapon;
        private GameObject attachedWeaponObject;
        private GameObject lastProjectileObject;
        private int currentSlotNumber;
        private int comboIndex;
        private float lastComboTime;
        private int clickSkillIndex;
        private float lastClickSkillTime;
        private bool clickComboWindowActive;
        private float clickComboReadyAt;
        private float clickComboExpiresAt;
        private bool queuedClickCombo;
        private int queuedClickComboSkillId;
        private Coroutine weaponSwitchCoroutine;
        private bool isSwitchingWeapon;
        private bool initialized;

        public PotcoWeaponCatalog Catalog => catalog;
        public PotcoWeaponDefinition CurrentWeapon => currentWeapon;
        public int CurrentWeaponId => currentWeapon != null ? currentWeapon.ItemId : 0;
        public int CurrentSlotNumber => currentSlotNumber;
        public bool IsSwitchingWeapon => isSwitchingWeapon;
        public bool IsWeaponDrawn { get; private set; }
        public GameObject AttachedWeaponObject => attachedWeaponObject;
        public GameObject LastProjectileObject => lastProjectileObject;
        public string LastSkillAnimationName { get; private set; } = string.Empty;
        public string LastWeaponAnimationName { get; private set; } = string.Empty;
        public IReadOnlyList<string> WeaponAnimationHistory => weaponAnimationHistory;
        public IReadOnlyList<string> Messages => messages;
        public IReadOnlyList<string> MissingAssets => visualResolver != null ? visualResolver.MissingResources : Array.Empty<string>();

        private void Awake()
        {
            EnsureInitialized();
            if (createHud && GetComponent<PotcoWeaponHud>() == null)
                gameObject.AddComponent<PotcoWeaponHud>();
        }

        private void OnDestroy()
        {
            DestroyAttachedWeapon();
            DestroyLastProjectile();
        }

        private void Update()
        {
            if (!Application.isPlaying || !EnsureInitialized())
                return;

            ProcessQueuedClickCombo();
            HandleFKeyInput();
            HandleSkillInput();
        }

        public void InitializeForTests(PotcoInventoryController inventory)
        {
            inventoryController = inventory;
            initialized = false;
            EnsureInitialized();
        }

        public bool EnsureInitialized()
        {
            if (initialized)
                return true;

            inventoryController = inventoryController != null ? inventoryController : GetComponent<PotcoInventoryController>();
            if (inventoryController == null)
                inventoryController = gameObject.AddComponent<PotcoInventoryController>();

            if (!inventoryController.EnsureLoaded())
            {
                AddMessage(inventoryController.LoadError);
                return false;
            }

            try
            {
                catalog = PotcoWeaponCatalog.LoadFromAssetsPath(Application.dataPath, inventoryController.Catalog);
                visualResolver = new PotcoWeaponVisualResolver();
                animationPlayer = GetComponent<SimpleAnimationPlayer>() ?? GetComponentInChildren<SimpleAnimationPlayer>();
                initialized = true;

                if (currentWeapon == null)
                    SelectFirstEquippedWeapon();
                return true;
            }
            catch (Exception ex)
            {
                AddMessage(ex.Message);
                Debug.LogError($"POTCO weapon controller failed to initialize: {ex}");
                return false;
            }
        }

        public bool SelectWeaponSlot(int slotNumber)
        {
            if (!EnsureInitialized())
                return false;

            int location = SlotToLocation(slotNumber);
            if (!PotcoInventoryLocations.EquipWeapons.Contains(location))
            {
                AddMessage($"Weapon slot F{slotNumber} is outside the reference weapon range.");
                return false;
            }

            PotcoInventoryItemStack stack = inventoryController.Inventory.GetItemAt(location);
            if (stack == null)
            {
                AddMessage($"No weapon is equipped in F{slotNumber}.");
                return false;
            }

            if (!catalog.TryGetWeapon(stack.ItemId, out PotcoWeaponDefinition definition))
            {
                AddMessage($"{stack.Definition.EffectiveDisplayName} is not a usable weapon.");
                return false;
            }

            if (currentSlotNumber == slotNumber && currentWeapon != null && currentWeapon.ItemId == definition.ItemId)
                return true;

            bool wasDrawn = IsWeaponDrawn;
            PotcoWeaponDefinition previousWeapon = currentWeapon;
            if (wasDrawn)
            {
                if (Application.isPlaying && isActiveAndEnabled)
                {
                    if (weaponSwitchCoroutine != null)
                        StopCoroutine(weaponSwitchCoroutine);

                    weaponSwitchCoroutine = StartCoroutine(SwitchDrawnWeapon(slotNumber, definition, previousWeapon));
                    return true;
                }

                PlayPutAwayAnimation(previousWeapon);
                DestroyAttachedWeapon();
            }

            SetCurrentWeaponSelection(slotNumber, definition);

            if (wasDrawn)
                DrawCurrentWeapon();

            return true;
        }

        public PotcoWeaponUseResult ToggleCurrentWeaponDrawn()
        {
            if (!EnsureInitialized())
                return PotcoWeaponUseResult.Fail("Weapon controller is not initialized.");

            if (currentWeapon == null && !SelectFirstEquippedWeapon())
                return PotcoWeaponUseResult.Fail("No equipped weapon is available.");

            return IsWeaponDrawn ? PutAwayCurrentWeapon() : DrawCurrentWeapon();
        }

        public PotcoWeaponUseResult DrawCurrentWeapon()
        {
            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            Transform hand = FindWeaponRightJoint();
            DestroyAttachedWeapon();
            attachedWeaponObject = visualResolver.ResolveOrCreateWeaponInstance(currentWeapon.ModelName, hand);
            ApplyAttachmentPose(attachedWeaponObject, currentWeapon.AttachmentPose);
            IsWeaponDrawn = true;

            ApplyAnimationOverride(currentWeapon);
            PlayWeaponAnimation(currentWeapon.DrawAnimation, WrapMode.Once, 0.08f, 0.35f);
            AddMessage($"Drew {currentWeapon.DisplayName}.");
            return PotcoWeaponUseResult.Ok("Weapon drawn.");
        }

        public PotcoWeaponUseResult PutAwayCurrentWeapon()
        {
            if (isSwitchingWeapon)
                return PotcoWeaponUseResult.Fail("Weapon switch is in progress.", PotcoCombatResult.Delay);

            if (!IsWeaponDrawn)
                return PotcoWeaponUseResult.Ok("Weapon already put away.");

            PlayPutAwayAnimation(currentWeapon);

            DestroyAttachedWeapon();
            IsWeaponDrawn = false;
            if (animationPlayer != null)
                animationPlayer.ClearWeaponAnimationOverride();
            AddMessage("Weapon put away.");
            return PotcoWeaponUseResult.Ok("Weapon put away.");
        }

        public PotcoWeaponUseResult UsePrimarySkill()
        {
            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            ProcessQueuedClickCombo();
            bool usesTimedCombo = UsesTimedClickCombo();
            PotcoClickComboInputState inputState = usesTimedCombo
                ? ResolveClickComboInputState(clickComboWindowActive, Time.time, clickComboReadyAt, clickComboExpiresAt)
                : PotcoClickComboInputState.Start;

            if (inputState == PotcoClickComboInputState.Expired)
                ResetClickComboWindow();

            PotcoWeaponSkill clickSkill = PeekNextClickSkill();
            if (usesTimedCombo && inputState == PotcoClickComboInputState.Active)
                return QueueClickCombo(clickSkill);

            PotcoWeaponUseResult result = UseSkill(clickSkill != null ? clickSkill.SkillId : currentWeapon.PrimarySkillId, true, false);
            if (result.Success)
                AdvanceClickSkill(usesTimedCombo);
            return result;
        }

        public PotcoWeaponUseResult UseSecondarySkill()
        {
            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            int skillId = currentWeapon.SecondarySkillId;
            return UseSkill(skillId);
        }

        public PotcoWeaponUseResult UseSkill(int skillId)
        {
            return UseSkill(skillId, false, false);
        }

        private PotcoWeaponUseResult UseSkill(int skillId, bool useClickAttackRecoveryCap, bool continueClickComboClip)
        {
            if (!EnsureInitialized())
                return PotcoWeaponUseResult.Fail("Weapon controller is not initialized.");

            if (isSwitchingWeapon)
                return PotcoWeaponUseResult.Fail("Weapon switch is in progress.", PotcoCombatResult.Delay);

            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            if (!IsWeaponDrawn)
            {
                PotcoWeaponUseResult draw = DrawCurrentWeapon();
                if (!draw.Success)
                    return draw;
            }

            PotcoWeaponSkill skill = ResolveSkill(skillId);
            if (skill == null)
                return PotcoWeaponUseResult.Fail($"Skill {skillId} is not available for {currentWeapon.DisplayName}.");

            return UseResolvedSkill(skill, ResolveAutoTarget(skill), useClickAttackRecoveryCap, continueClickComboClip);
        }

        public PotcoWeaponUseResult UseNumberSkill(int number)
        {
            if (!EnsureInitialized())
                return PotcoWeaponUseResult.Fail("Weapon controller is not initialized.");

            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            int index = number - 1;
            if (index < 0 || index >= currentWeapon.Skills.Count || index >= 9)
                return PotcoWeaponUseResult.Fail($"No weapon skill is assigned to {number}.");

            return UseSkill(currentWeapon.Skills[index].SkillId);
        }

        public PotcoWeaponUseResult UseSkillOnTarget(int skillId, PotcoCombatTarget target)
        {
            if (!EnsureInitialized())
                return PotcoWeaponUseResult.Fail("Weapon controller is not initialized.");

            if (isSwitchingWeapon)
                return PotcoWeaponUseResult.Fail("Weapon switch is in progress.", PotcoCombatResult.Delay);

            if (currentWeapon == null)
                return PotcoWeaponUseResult.Fail("No weapon is selected.");

            if (!IsWeaponDrawn)
            {
                PotcoWeaponUseResult draw = DrawCurrentWeapon();
                if (!draw.Success)
                    return draw;
            }

            PotcoWeaponSkill skill = ResolveSkill(skillId);
            if (skill == null)
                return PotcoWeaponUseResult.Fail($"Skill {skillId} is not available for {currentWeapon.DisplayName}.");

            return UseResolvedSkill(skill, target, false, false);
        }

        private PotcoWeaponUseResult UseResolvedSkill(PotcoWeaponSkill skill, PotcoCombatTarget target, bool useClickAttackRecoveryCap, bool continueClickComboClip)
        {
            PotcoWeaponUseResult targetValidation = ValidateTarget(skill, target);
            if (!targetValidation.Success)
                return targetValidation;

            if (nextSkillUseTime.TryGetValue(skill.SkillId, out float availableAt) && Time.time < availableAt)
                return PotcoWeaponUseResult.Fail($"{skill.Name} is recharging.", PotcoCombatResult.NotRecharged, skill, target);

            nextSkillUseTime[skill.SkillId] = Time.time + skill.RechargeSeconds;
            string animationName = ResolveSkillAnimation(skill);
            string previousSkillAnimationName = LastSkillAnimationName;
            LastSkillAnimationName = animationName;
            bool restartAnimation = ShouldRestartClickComboAnimation(previousSkillAnimationName, animationName, continueClickComboClip);
            PlayWeaponAnimation(
                animationName,
                WrapMode.Once,
                0.05f,
                0.45f,
                ResolveSkillAnimationLockCapSeconds(useClickAttackRecoveryCap),
                ShouldPreferInMotionSkillAnimation(animationName),
                restartAnimation);
            SpawnProjectileVisual(skill, target);

            float damage = 0f;
            float healing = 0f;
            IReadOnlyList<PotcoCombatTarget> affectedTargets = ResolveEffectTargets(skill, target);
            foreach (PotcoCombatTarget affectedTarget in affectedTargets)
            {
                float appliedEffect = ApplySkillTargetHealthEffect(skill, affectedTarget);
                if (appliedEffect < 0f)
                    damage += -appliedEffect;
                else
                    healing += appliedEffect;

                ApplySkillStatusEffect(skill, affectedTarget);
            }

            if (ShouldApplySelfEffects(skill))
            {
                PotcoCombatTarget selfTarget = ResolveSelfTarget();
                float appliedSelfEffect = ApplySkillSelfHealthEffect(skill, selfTarget);
                if (appliedSelfEffect < 0f)
                    damage += -appliedSelfEffect;
                else
                    healing += appliedSelfEffect;

                ApplySkillSelfStatusEffect(skill, selfTarget);
            }

            if (damage > 0f)
                AddMessage($"Used {skill.Name} for {damage:0.#} damage.");
            else if (healing > 0f)
                AddMessage($"Used {skill.Name} for {healing:0.#} healing.");
            else
                AddMessage($"Used {skill.Name}.");

            PotcoCombatResult combatResult = ResolveCombatResult(skill, target, damage, healing);
            return PotcoWeaponUseResult.Ok("Skill used.", skill, combatResult, target, damage);
        }

        public float GetSkillCooldownRemaining(int skillId)
        {
            if (skillId <= 0 || !nextSkillUseTime.TryGetValue(skillId, out float availableAt))
                return 0f;

            return Mathf.Max(0f, availableAt - Time.time);
        }

        public float GetSkillCooldownRatio(PotcoWeaponSkill skill)
        {
            if (skill == null || skill.RechargeSeconds <= 0f)
                return 0f;

            return Mathf.Clamp01(GetSkillCooldownRemaining(skill.SkillId) / skill.RechargeSeconds);
        }

        private void HandleFKeyInput()
        {
            if (isSwitchingWeapon)
                return;

            for (int slot = 1; slot <= PotcoInventoryLocations.EquipWeapons.Count; slot++)
            {
                KeyCode key = (KeyCode)((int)KeyCode.F1 + slot - 1);
                if (!Input.GetKeyDown(key))
                    continue;

                if (currentSlotNumber == slot && currentWeapon != null)
                {
                    ToggleCurrentWeaponDrawn();
                    return;
                }

                bool selected = SelectWeaponSlot(slot);
                if (selected && !IsWeaponDrawn)
                    DrawCurrentWeapon();
                return;
            }
        }

        private void HandleSkillInput()
        {
            if (isSwitchingWeapon || !IsWeaponDrawn || currentWeapon == null)
                return;

            if (allowMousePrimarySkill && Input.GetMouseButtonDown(0))
                UsePrimarySkill();

            if (!allowKeyboardSkillNumbers)
                return;

            for (int i = 0; i < currentWeapon.Skills.Count && i < 9; i++)
            {
                KeyCode alphaKey = (KeyCode)((int)KeyCode.Alpha1 + i);
                KeyCode keypadKey = (KeyCode)((int)KeyCode.Keypad1 + i);
                if (Input.GetKeyDown(alphaKey) || Input.GetKeyDown(keypadKey))
                    UseNumberSkill(i + 1);
            }
        }

        private bool SelectFirstEquippedWeapon()
        {
            foreach (int location in PotcoInventoryLocations.Expand(PotcoInventoryLocations.EquipWeapons))
            {
                PotcoInventoryItemStack stack = inventoryController.Inventory.GetItemAt(location);
                if (stack == null || !catalog.TryGetWeapon(stack.ItemId, out PotcoWeaponDefinition definition))
                    continue;

                SetCurrentWeaponSelection(LocationToSlot(location), definition);
                return true;
            }

            return false;
        }

        private PotcoWeaponSkill ResolveSkill(int skillId)
        {
            if (skillId <= 0)
            {
                if (currentWeapon.ClickSkills.Count > 0)
                    return currentWeapon.ClickSkills[0];

                return currentWeapon.Skills.Count > 0 ? currentWeapon.Skills[0] : null;
            }

            foreach (PotcoWeaponSkill skill in currentWeapon.ClickSkills)
            {
                if (skill.SkillId == skillId)
                    return skill;
            }

            foreach (PotcoWeaponSkill skill in currentWeapon.Skills)
            {
                if (skill.SkillId == skillId)
                    return skill;
            }

            foreach (PotcoWeaponSkill skill in currentWeapon.AllSkills)
            {
                if (skill.SkillId == skillId)
                    return skill;
            }

            return null;
        }

        private PotcoWeaponUseResult ValidateTarget(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null)
                return PotcoWeaponUseResult.Fail("No skill is selected.");

            if (target == null)
            {
                return skill.NeedTarget
                    ? PotcoWeaponUseResult.Ok("No target resolved.", skill, PotcoCombatResult.Miss)
                    : PotcoWeaponUseResult.Ok("No target required.", skill);
            }

            if (!target.IsAlive)
                return PotcoWeaponUseResult.Fail($"{target.name} is already defeated.", PotcoCombatResult.NotAvailable, skill, target);

            float distance = Vector3.Distance(transform.position, target.transform.position);
            float deadzone = GetModifiedAttackDeadzone();
            if (deadzone > 0f && distance < deadzone)
                return PotcoWeaponUseResult.Fail($"{target.name} is inside the weapon deadzone.", PotcoCombatResult.OutOfRange, skill, target);

            float range = GetModifiedSkillRange(skill);
            if (range > 0f && distance > range)
                return PotcoWeaponUseResult.Fail($"{target.name} is out of range.", PotcoCombatResult.OutOfRange, skill, target);

            return PotcoWeaponUseResult.Ok("Target valid.", skill, PotcoCombatResult.Hit, target);
        }

        private IReadOnlyList<PotcoCombatTarget> ResolveEffectTargets(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null)
                return Array.Empty<PotcoCombatTarget>();

            if (skill.AreaRadius <= 0f)
                return target != null ? new[] { target } : Array.Empty<PotcoCombatTarget>();

            Vector3 center = target != null ? target.transform.position : ResolveAreaCenter(skill);
            float radius = Mathf.Max(0f, skill.AreaRadius);
            List<PotcoCombatTarget> targets = new List<PotcoCombatTarget>();
            PotcoCombatTarget[] candidates = FindObjectsByType<PotcoCombatTarget>(FindObjectsSortMode.None);
            foreach (PotcoCombatTarget candidate in candidates)
            {
                if (candidate == null || !candidate.IsAlive)
                    continue;

                if (Vector3.Distance(center, candidate.transform.position) <= radius)
                    targets.Add(candidate);
            }

            return targets;
        }

        private Vector3 ResolveAreaCenter(PotcoWeaponSkill skill)
        {
            Vector3 forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
            float distance = GetModifiedSkillRange(skill);
            return transform.position + forward * distance;
        }

        private float ApplySkillTargetHealthEffect(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null || target == null)
                return 0f;

            float effect = CalculateSkillTargetHealthEffect(skill);
            if (effect < 0f)
                return -target.ApplyWeaponDamage(-effect, gameObject, currentWeapon, skill);

            if (effect > 0f)
                return target.ApplyWeaponHealing(effect, gameObject, currentWeapon, skill);

            return 0f;
        }

        private PotcoWeaponStatusEffect ApplySkillStatusEffect(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null || target == null || skill.EffectFlag <= 0)
                return null;

            return target.ApplyWeaponStatusEffect(skill.EffectFlag, skill.DurationSeconds, gameObject, currentWeapon, skill);
        }

        private bool ShouldApplySelfEffects(PotcoWeaponSkill skill)
        {
            if (skill == null)
                return false;

            return skill.SelfUse || !Mathf.Approximately(skill.SelfHealth, 0f);
        }

        private PotcoCombatTarget ResolveSelfTarget()
        {
            PotcoCombatTarget selfTarget = GetComponent<PotcoCombatTarget>();
            if (selfTarget != null)
                return selfTarget;

            selfTarget = GetComponentInParent<PotcoCombatTarget>();
            if (selfTarget != null)
                return selfTarget;

            selfTarget = GetComponentInChildren<PotcoCombatTarget>();
            return selfTarget != null ? selfTarget : gameObject.AddComponent<PotcoCombatTarget>();
        }

        private float ApplySkillSelfHealthEffect(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null || target == null)
                return 0f;

            float effect = CalculateSkillSelfHealthEffect(skill);
            if (effect < 0f)
                return -target.ApplyWeaponDamage(-effect, gameObject, currentWeapon, skill);

            if (effect > 0f)
                return target.ApplyWeaponHealing(effect, gameObject, currentWeapon, skill);

            return 0f;
        }

        private PotcoWeaponStatusEffect ApplySkillSelfStatusEffect(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null || target == null || !skill.SelfUse || skill.EffectFlag <= 0)
                return null;

            return target.ApplyWeaponStatusEffect(skill.EffectFlag, skill.DurationSeconds, gameObject, currentWeapon, skill);
        }

        private float CalculateSkillTargetHealthEffect(PotcoWeaponSkill skill)
        {
            if (skill == null || currentWeapon == null || Mathf.Approximately(skill.TargetHealth, 0f))
                return 0f;

            float weaponPower = currentWeapon.Item != null ? currentWeapon.Item.Power : 0f;
            return skill.TargetHealth * skill.NumHits * GetReferenceWeaponDamageScale(currentWeapon) * (1f + weaponPower * WeaponPowerMultiplier);
        }

        private float CalculateSkillSelfHealthEffect(PotcoWeaponSkill skill)
        {
            if (skill == null || currentWeapon == null || Mathf.Approximately(skill.SelfHealth, 0f))
                return 0f;

            float weaponPower = currentWeapon.Item != null ? currentWeapon.Item.Power : 0f;
            return skill.SelfHealth * GetReferenceWeaponDamageScale(currentWeapon) * (1f + weaponPower * WeaponPowerMultiplier);
        }

        private static float GetReferenceWeaponDamageScale(PotcoWeaponDefinition weapon)
        {
            if (weapon == null)
                return 1f;

            return 1f;
        }

        private PotcoCombatTarget ResolveAutoTarget(PotcoWeaponSkill skill)
        {
            float range = GetModifiedSkillRange(skill);
            if (range <= 0f)
                range = 6f;

            float deadzone = GetModifiedAttackDeadzone();
            PotcoCombatTarget raycastTarget = ResolveCameraTarget(range, deadzone);
            if (raycastTarget != null)
                return raycastTarget;

            return ResolveForwardTarget(range, deadzone);
        }

        private PotcoCombatTarget ResolveCameraTarget(float range, float deadzone)
        {
            Camera camera = Camera.main;
            if (camera == null)
                return null;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, range, weaponTargetMask, QueryTriggerInteraction.Collide))
                return null;

            PotcoCombatTarget target = hit.collider.GetComponentInParent<PotcoCombatTarget>();
            if (target == null || IsInsideDeadzone(target, deadzone))
                return null;

            return target;
        }

        private PotcoCombatTarget ResolveForwardTarget(float range, float deadzone)
        {
            PotcoCombatTarget[] targets = FindObjectsByType<PotcoCombatTarget>(FindObjectsSortMode.None);
            PotcoCombatTarget best = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
            float minDot = Mathf.Cos(maxAutoTargetAngle * Mathf.Deg2Rad);

            foreach (PotcoCombatTarget target in targets)
            {
                if (target == null || !target.IsAlive)
                    continue;

                Vector3 offset = target.transform.position - origin;
                float distance = offset.magnitude;
                if (distance > range || distance <= 0.001f || (deadzone > 0f && distance < deadzone))
                    continue;

                if (Vector3.Dot(forward, offset / distance) < minDot)
                    continue;

                if (distance < bestDistance)
                {
                    best = target;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private float GetModifiedSkillRange(PotcoWeaponSkill skill)
        {
            float range = skill != null && skill.Range > 0f ? skill.Range : 0f;
            if (range <= 0f || currentWeapon?.Item == null)
                return range;

            if (currentWeapon.Item.ItemType == ItemTypeGun)
                return range * GetGunRangeMultiplier(currentWeapon.Item.Subtype);

            return range;
        }

        private static float GetGunRangeMultiplier(int subtype)
        {
            switch (subtype)
            {
                case ItemSubtypePistol:
                case ItemSubtypeRepeater:
                    return GunMediumRangeMultiplier;
                case ItemSubtypeMusket:
                case ItemSubtypeBayonet:
                    return GunLongRangeMultiplier;
                case ItemSubtypeBlunderbuss:
                default:
                    return GunShortRangeMultiplier;
            }
        }

        private float GetModifiedAttackDeadzone()
        {
            if (currentWeapon?.Item == null || currentWeapon.Item.ItemType != ItemTypeGun)
                return 0f;

            return HasReferenceGunDeadzone(currentWeapon.Item.Subtype) ? GunDeadzoneRange : 0f;
        }

        private static bool HasReferenceGunDeadzone(int subtype)
        {
            return subtype == ItemSubtypeMusket || subtype == ItemSubtypeBayonet;
        }

        private bool IsInsideDeadzone(PotcoCombatTarget target, float deadzone)
        {
            return target != null && deadzone > 0f && Vector3.Distance(transform.position, target.transform.position) < deadzone;
        }

        private void SpawnProjectileVisual(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (!ShouldSpawnProjectileVisual(skill, target))
                return;

            DestroyLastProjectile();

            string modelName = ResolveProjectileModelName();
            GameObject prefab = visualResolver != null ? visualResolver.ResolveProjectilePrefab(modelName) : null;
            lastProjectileObject = prefab != null
                ? Instantiate(prefab)
                : PotcoWeaponVisualResolver.CreateFallbackProjectile(modelName);

            lastProjectileObject.name = $"POTCO Projectile {modelName}";
            Vector3 start = attachedWeaponObject != null ? attachedWeaponObject.transform.position : FindWeaponRightJoint().position;
            Vector3 end = target != null
                ? target.transform.position + Vector3.up * 0.65f
                : ResolveProjectileFallbackEnd(skill, start);
            PositionProjectileVisual(lastProjectileObject, start, end);
        }

        private bool ShouldSpawnProjectileVisual(PotcoWeaponSkill skill, PotcoCombatTarget target)
        {
            if (skill == null || currentWeapon == null)
                return false;

            return skill.Range > 10f ||
                   skill.ProjectilePower > 0f ||
                   skill.AttackClass == 2 ||
                   skill.AttackClass == 3 ||
                   skill.AttackClass == 4 ||
                   currentWeapon.Class == PotcoWeaponClass.Pistol ||
                   currentWeapon.Class == PotcoWeaponClass.Gun ||
                   currentWeapon.Class == PotcoWeaponClass.Bayonet ||
                   currentWeapon.Class == PotcoWeaponClass.Grenade ||
                   currentWeapon.Class == PotcoWeaponClass.PowderKeg ||
                   currentWeapon.Class == PotcoWeaponClass.Wand;
        }

        private Vector3 ResolveProjectileFallbackEnd(PotcoWeaponSkill skill, Vector3 start)
        {
            Vector3 forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
            float distance = GetModifiedSkillRange(skill);
            if (distance <= 0f)
                distance = currentWeapon != null && (currentWeapon.Class == PotcoWeaponClass.Grenade || currentWeapon.Class == PotcoWeaponClass.PowderKeg) ? 12f : 8f;

            return start + forward * Mathf.Clamp(distance, 4f, 40f);
        }

        private string ResolveProjectileModelName()
        {
            if (currentWeapon == null)
                return "projectile";

            switch (currentWeapon.Class)
            {
                case PotcoWeaponClass.Grenade:
                    return "grenade";
                case PotcoWeaponClass.PowderKeg:
                    return string.IsNullOrEmpty(currentWeapon.ModelName) ? "pir_m_hnd_bom_barrelDynamite" : currentWeapon.ModelName;
                case PotcoWeaponClass.Dagger:
                    return string.IsNullOrEmpty(currentWeapon.ModelName) ? "dagger" : currentWeapon.ModelName;
                case PotcoWeaponClass.Wand:
                case PotcoWeaponClass.Doll:
                    return "voodooRing";
                case PotcoWeaponClass.Pistol:
                case PotcoWeaponClass.Gun:
                    return "cannonTrail";
                default:
                    return "projectile";
            }
        }

        private static void PositionProjectileVisual(GameObject projectile, Vector3 start, Vector3 end)
        {
            if (projectile == null)
                return;

            Vector3 delta = end - start;
            float distance = Mathf.Max(0.001f, delta.magnitude);
            Vector3 direction = delta / distance;
            projectile.transform.position = start + delta * 0.5f;
            projectile.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            LineRenderer line = projectile.GetComponentInChildren<LineRenderer>(true);
            if (line != null)
            {
                line.SetPosition(0, start);
                line.SetPosition(1, end);
            }

            Transform tip = projectile.transform.Find("Fallback Impact Tip");
            if (tip != null)
                tip.position = end;
        }

        private string ResolveSkillAnimation(PotcoWeaponSkill skill)
        {
            string weaponSpecificAnimation = ResolveWeaponSpecificSkillAnimation(skill);
            if (!string.IsNullOrEmpty(weaponSpecificAnimation))
                return weaponSpecificAnimation;

            if (!string.IsNullOrEmpty(skill.AnimationName))
                return skill.AnimationName;

            string comboAnimation = TakeNextComboAnimation();
            if (!string.IsNullOrEmpty(comboAnimation))
                return comboAnimation;

            return currentWeapon.DrawAnimation;
        }

        private string ResolveWeaponSpecificSkillAnimation(PotcoWeaponSkill skill)
        {
            if (skill == null || currentWeapon == null)
                return string.Empty;

            switch (currentWeapon.Class)
            {
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                    return IsCutlassAnimatedSkill(skill.SkillId) ? TakeNextComboAnimation() : string.Empty;
                case PotcoWeaponClass.Gun:
                    if (skill.AttackClass == 2)
                        return skill.SkillId == MusketTakeAimSkill ? "rifle_fight_shoot_high" : "rifle_fight_shoot_hip";
                    return string.Empty;
                case PotcoWeaponClass.Bayonet:
                    return ResolveBayonetSkillAnimation(skill);
                case PotcoWeaponClass.PowderKeg:
                    if (skill.SkillId == 12506)
                        return "bigbomb_charge_throw";
                    if (IsGrenadeThrowSkill(skill.SkillId))
                        return "bigbomb_throw";
                    return string.Empty;
                default:
                    return string.Empty;
            }
        }

        private string ResolveBayonetSkillAnimation(PotcoWeaponSkill skill)
        {
            switch (skill.SkillId)
            {
                case BayonetShootSkill:
                    return "rifle_fight_shoot_hip";
                case BayonetStabSkill:
                    return "bayonet_attackA";
                case BayonetRushSkill:
                    return "bayonet_attackC";
                case BayonetBashSkill:
                    return "bayonet_attackB";
                default:
                    return skill.AttackClass == 2 ? "rifle_fight_shoot_hip" : string.Empty;
            }
        }

        private string TakeNextComboAnimation()
        {
            if (currentWeapon == null)
                return string.Empty;

            if (Time.time - lastComboTime > comboResetSeconds)
                comboIndex = 0;

            lastComboTime = Time.time;
            return currentWeapon.GetComboAnimation(comboIndex++);
        }

        private static bool IsCutlassAnimatedSkill(int skillId)
        {
            return skillId == 12100 ||
                   skillId == 12101 ||
                   skillId == 12102 ||
                   skillId == 12103 ||
                   skillId == 12104 ||
                   skillId == 12107 ||
                   skillId == 12108 ||
                   skillId == 12109 ||
                   skillId == 12110;
        }

        private static bool IsGrenadeThrowSkill(int skillId)
        {
            return skillId >= 12500 && skillId <= 12505;
        }

        private PotcoWeaponSkill PeekNextClickSkill()
        {
            if (currentWeapon == null || currentWeapon.ClickSkills.Count == 0)
                return null;

            if (!UsesTimedClickCombo() && Time.time - lastClickSkillTime > comboResetSeconds)
                clickSkillIndex = 0;

            return currentWeapon.GetClickSkill(clickSkillIndex);
        }

        private void AdvanceClickSkill(bool startTimedComboWindow)
        {
            if (currentWeapon == null || currentWeapon.ClickSkills.Count == 0)
                return;

            clickSkillIndex++;
            lastClickSkillTime = Time.time;
            if (startTimedComboWindow)
                StartClickComboWindow(Time.time, ReferenceClickAttackRecoverySeconds);
        }

        private PotcoWeaponUseResult QueueClickCombo(PotcoWeaponSkill skill)
        {
            PotcoWeaponSkill queuedSkill = skill ?? ResolveSkill(currentWeapon.PrimarySkillId);
            PotcoCombatTarget target = ResolveAutoTarget(queuedSkill);
            if (queuedClickCombo)
                return PotcoWeaponUseResult.Fail("Combo input is already queued.", PotcoCombatResult.Delay, queuedSkill, target);

            if (queuedSkill == null)
                return PotcoWeaponUseResult.Fail("No combo skill is available.", PotcoCombatResult.NotAvailable, null, target);

            queuedClickCombo = true;
            queuedClickComboSkillId = queuedSkill.SkillId;
            return PotcoWeaponUseResult.Ok("Combo queued.", queuedSkill, PotcoCombatResult.Delay, target);
        }

        private void ProcessQueuedClickCombo()
        {
            if (!queuedClickCombo || !clickComboWindowActive || Time.time < clickComboExpiresAt)
                return;

            int skillId = queuedClickComboSkillId;
            queuedClickCombo = false;
            queuedClickComboSkillId = 0;

            PotcoWeaponUseResult result = UseSkill(skillId, true, true);
            if (result.Success)
                AdvanceClickSkill(true);
            else
                ResetClickComboWindow();
        }

        private void ResetSkillChainState()
        {
            comboIndex = 0;
            lastComboTime = 0f;
            clickSkillIndex = 0;
            lastClickSkillTime = 0f;
            ResetClickComboWindow();
        }

        private bool UsesTimedClickCombo()
        {
            if (currentWeapon == null || currentWeapon.ClickSkills.Count <= 1)
                return false;

            switch (currentWeapon.Class)
            {
                case PotcoWeaponClass.Melee:
                case PotcoWeaponClass.Sword:
                case PotcoWeaponClass.Bayonet:
                case PotcoWeaponClass.Dagger:
                case PotcoWeaponClass.DualCutlass:
                case PotcoWeaponClass.Foil:
                case PotcoWeaponClass.MonsterMelee:
                case PotcoWeaponClass.Torch:
                    return true;
                default:
                    return false;
            }
        }

        private void StartClickComboWindow(float now, float attackDurationSeconds)
        {
            clickComboWindowActive = true;
            clickComboReadyAt = now + ReferenceComboReadyDelaySeconds(attackDurationSeconds);
            clickComboExpiresAt = now + Mathf.Max(0f, attackDurationSeconds);
        }

        private void ResetClickComboWindow()
        {
            clickComboWindowActive = false;
            clickComboReadyAt = 0f;
            clickComboExpiresAt = 0f;
            queuedClickCombo = false;
            queuedClickComboSkillId = 0;
            clickSkillIndex = 0;
        }

        public static float ReferenceComboReadyDelaySeconds(float attackDurationSeconds)
        {
            return Mathf.Max(0f, attackDurationSeconds) * 0.4f;
        }

        public static PotcoClickComboInputState ResolveClickComboInputState(bool windowActive, float now, float readyAt, float expiresAt)
        {
            if (!windowActive)
                return PotcoClickComboInputState.Start;

            if (now <= expiresAt)
                return PotcoClickComboInputState.Active;

            return PotcoClickComboInputState.Expired;
        }

        private void SetCurrentWeaponSelection(int slotNumber, PotcoWeaponDefinition definition)
        {
            currentSlotNumber = slotNumber;
            currentWeapon = definition;
            ResetSkillChainState();
        }

        private IEnumerator SwitchDrawnWeapon(int slotNumber, PotcoWeaponDefinition definition, PotcoWeaponDefinition previousWeapon)
        {
            isSwitchingWeapon = true;
            PlayPutAwayAnimation(previousWeapon);
            if (previousWeapon != null)
                AddMessage($"Putting away {previousWeapon.DisplayName}.");

            yield return new WaitForSeconds(WeaponSwitchPutAwaySeconds);

            DestroyAttachedWeapon();
            SetCurrentWeaponSelection(slotNumber, definition);
            PotcoWeaponUseResult drawResult = DrawCurrentWeapon();
            if (!drawResult.Success)
                AddMessage(drawResult.Message);

            isSwitchingWeapon = false;
            weaponSwitchCoroutine = null;
        }

        private void PlayPutAwayAnimation(PotcoWeaponDefinition definition)
        {
            if (definition == null)
                return;

            PlayWeaponAnimation(definition.PutAwayAnimation, WrapMode.Once, 0.08f, WeaponSwitchPutAwaySeconds);
        }

        private static void ApplyAttachmentPose(GameObject weaponObject, PotcoWeaponAttachmentPose pose)
        {
            if (weaponObject == null || pose == null)
                return;

            weaponObject.transform.localPosition = pose.LocalPosition;
            weaponObject.transform.localRotation = Quaternion.Euler(pose.LocalEulerAngles);
            weaponObject.transform.localScale = pose.LocalScale;
        }

        private void ApplyAnimationOverride(PotcoWeaponDefinition definition)
        {
            if (animationPlayer == null || definition == null)
                return;

            animationPlayer.SetWeaponAnimationOverride(
                definition.AnimationSet.Neutral,
                definition.AnimationSet.Walk,
                definition.AnimationSet.Run,
                definition.AnimationSet.WalkBack,
                definition.AnimationSet.StrafeLeft,
                definition.AnimationSet.StrafeRight);
        }

        public static float ResolveSkillAnimationLockCapSeconds(bool useClickAttackRecoveryCap)
        {
            return useClickAttackRecoveryCap ? ReferenceClickAttackRecoverySeconds : 0f;
        }

        public static bool ShouldPreferInMotionSkillAnimation(string animationName)
        {
            return !string.IsNullOrWhiteSpace(animationName);
        }

        public static bool ShouldRestartClickComboAnimation(string previousAnimationName, string nextAnimationName, bool continueClickComboClip)
        {
            return !continueClickComboClip || !string.Equals(previousAnimationName, nextAnimationName, StringComparison.Ordinal);
        }

        private void PlayWeaponAnimation(string animationName, WrapMode wrapMode, float transition, float lockSeconds)
        {
            PlayWeaponAnimation(animationName, wrapMode, transition, lockSeconds, 0f, false, true);
        }

        private void PlayWeaponAnimation(
            string animationName,
            WrapMode wrapMode,
            float transition,
            float lockSeconds,
            float maxLockSeconds,
            bool preferInMotionVariant,
            bool restartIfAlreadyPlaying)
        {
            if (string.IsNullOrEmpty(animationName))
                return;

            RecordWeaponAnimation(animationName);
            if (animationPlayer == null)
                return;

            if (!animationPlayer.TryPlayExternalAnimation(animationName, wrapMode, transition, lockSeconds, maxLockSeconds, preferInMotionVariant, restartIfAlreadyPlaying))
                AddMessage($"Missing weapon animation clip: {animationName}.");
        }

        private void RecordWeaponAnimation(string animationName)
        {
            LastWeaponAnimationName = animationName;
            weaponAnimationHistory.Add(animationName);
            while (weaponAnimationHistory.Count > 16)
                weaponAnimationHistory.RemoveAt(0);
        }

        private static PotcoCombatResult ResolveCombatResult(PotcoWeaponSkill skill, PotcoCombatTarget target, float damage, float healing)
        {
            if (target != null || damage > 0f || healing > 0f || skill == null || !skill.NeedTarget)
                return PotcoCombatResult.Hit;

            return PotcoCombatResult.Miss;
        }

        private Transform FindWeaponRightJoint()
        {
            Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in allTransforms)
            {
                string lower = child.name.ToLowerInvariant();
                if (lower == "weapon_right" || lower == "weaponright" || lower == "righthand" || lower == "right_hand")
                    return child;
            }

            Transform fallback = transform.Find("weapon_right");
            if (fallback == null)
            {
                GameObject fallbackObject = new GameObject("weapon_right");
                fallbackObject.transform.SetParent(transform, false);
                fallbackObject.transform.localPosition = new Vector3(0.35f, 1.25f, -0.1f);
                fallbackObject.transform.localRotation = Quaternion.identity;
                fallback = fallbackObject.transform;
            }

            AddMessage("Missing character weapon_right joint; attached weapon to fallback hand node.");
            return fallback;
        }

        private void DestroyAttachedWeapon()
        {
            if (attachedWeaponObject == null)
                return;

            if (Application.isPlaying)
                Destroy(attachedWeaponObject);
            else
                DestroyImmediate(attachedWeaponObject);

            attachedWeaponObject = null;
        }

        private void DestroyLastProjectile()
        {
            if (lastProjectileObject == null)
                return;

            if (Application.isPlaying)
                Destroy(lastProjectileObject);
            else
                DestroyImmediate(lastProjectileObject);

            lastProjectileObject = null;
        }

        private void AddMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            messages.Add(message);
            while (messages.Count > 8)
                messages.RemoveAt(0);
        }

        private static int SlotToLocation(int slotNumber)
        {
            return PotcoInventoryLocations.EquipWeapons.First + slotNumber - 1;
        }

        private static int LocationToSlot(int location)
        {
            return location - PotcoInventoryLocations.EquipWeapons.First + 1;
        }
    }
}
