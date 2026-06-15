using POTCO.Inventory;
using UnityEngine;

namespace POTCO.Combat
{
    [DisallowMultipleComponent]
    public sealed class PotcoWeaponHud : MonoBehaviour
    {
        [SerializeField] private bool visible = true;

        private PotcoWeaponController weaponController;
        private PotcoInventoryController inventoryController;
        private PotcoRuntimeGuiAssetResolver guiResolver;
        private GUIStyle slotStyle;
        private GUIStyle selectedSlotStyle;
        private GUIStyle skillSlotStyle;
        private GUIStyle labelStyle;
        private GUIStyle tooltipStyle;

        private void Awake()
        {
            weaponController = GetComponent<PotcoWeaponController>();
            inventoryController = GetComponent<PotcoInventoryController>();
            guiResolver = new PotcoRuntimeGuiAssetResolver();
        }

        private void OnGUI()
        {
            if (!visible || weaponController == null || inventoryController == null || !inventoryController.EnsureLoaded())
                return;

            EnsureStyles();
            Rect[] slots = BuildSlotRects(new Rect(0f, 0f, Screen.width, Screen.height), PotcoInventoryLocations.EquipWeapons.Count);
            for (int i = 0; i < slots.Length; i++)
            {
                int slotNumber = i + 1;
                int location = PotcoInventoryLocations.EquipWeapons.First + i;
                PotcoInventoryItemStack stack = inventoryController.Inventory.GetItemAt(location);
                bool selected = weaponController.CurrentSlotNumber == slotNumber;
                string tooltip = stack != null ? $"{stack.Definition.EffectiveDisplayName}  F{slotNumber}" : $"Empty weapon slot F{slotNumber}";
                GUI.Box(slots[i], new GUIContent(string.Empty, tooltip), selected ? selectedSlotStyle : slotStyle);
                DrawRarityAccent(slots[i], stack);

                string hotkey = $"F{slotNumber}";
                GUI.Label(new Rect(slots[i].x + 4f, slots[i].y + 2f, slots[i].width - 8f, 16f), hotkey, labelStyle);
                if (stack != null)
                {
                    Rect iconRect = new Rect(slots[i].x + 10f, slots[i].y + 18f, slots[i].width - 20f, slots[i].height - 26f);
                    if (!DrawIcon(iconRect, stack.Definition))
                    {
                        string name = stack.Definition.EffectiveDisplayName;
                        GUI.Label(new Rect(slots[i].x + 4f, slots[i].y + 20f, slots[i].width - 8f, slots[i].height - 24f), name, labelStyle);
                    }

                    DrawCooldownOverlay(iconRect, selected);
                }
            }

            DrawSkillStrip(new Rect(0f, 0f, Screen.width, Screen.height));

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 size = tooltipStyle.CalcSize(new GUIContent(GUI.tooltip));
                Rect rect = new Rect(Event.current.mousePosition.x + 16f, Event.current.mousePosition.y - 8f, size.x + 18f, size.y + 10f);
                GUI.Box(rect, GUI.tooltip, tooltipStyle);
            }
        }

        public static Rect[] BuildSlotRects(Rect screenRect, int slotCount)
        {
            slotCount = Mathf.Max(0, slotCount);
            var rects = new Rect[slotCount];
            if (slotCount == 0)
                return rects;

            float slotSize = Mathf.Clamp(screenRect.width / 12f, 54f, 78f);
            float gap = 6f;
            float totalWidth = slotCount * slotSize + (slotCount - 1) * gap;
            float x = screenRect.x + (screenRect.width - totalWidth) * 0.5f;
            float y = screenRect.yMax - slotSize - 18f;

            for (int i = 0; i < slotCount; i++)
                rects[i] = new Rect(x + i * (slotSize + gap), y, slotSize, slotSize);
            return rects;
        }

        public static Rect[] BuildSkillRects(Rect screenRect, int skillCount, int weaponSlotCount)
        {
            skillCount = Mathf.Clamp(skillCount, 0, 9);
            var rects = new Rect[skillCount];
            if (skillCount == 0)
                return rects;

            float gap = screenRect.width < 420f ? 4f : 5f;
            float availableWidth = Mathf.Max(1f, screenRect.width - 16f);
            float preferredSize = Mathf.Clamp(screenRect.width / 18f, 40f, 54f);
            float widthFitSize = Mathf.Max(1f, (availableWidth - (skillCount - 1) * gap) / skillCount);
            float slotSize = Mathf.Min(preferredSize, widthFitSize);
            if (slotSize < 20f && widthFitSize >= 20f)
                slotSize = 20f;

            float totalWidth = skillCount * slotSize + (skillCount - 1) * gap;
            float x = screenRect.x + Mathf.Max(0f, (screenRect.width - totalWidth) * 0.5f);
            Rect[] weaponSlots = BuildSlotRects(screenRect, weaponSlotCount);
            float weaponTop = weaponSlots.Length > 0 ? weaponSlots[0].yMin : screenRect.yMax - 96f;
            float y = Mathf.Max(screenRect.y + 8f, weaponTop - slotSize - 8f);

            for (int i = 0; i < skillCount; i++)
                rects[i] = new Rect(x + i * (slotSize + gap), y, slotSize, slotSize);
            return rects;
        }

        private void EnsureStyles()
        {
            if (slotStyle != null)
                return;

            slotStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 11
            };
            selectedSlotStyle = new GUIStyle(slotStyle);
            skillSlotStyle = new GUIStyle(slotStyle)
            {
                fontSize = 10
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                fontSize = 10,
                normal = { textColor = Color.white }
            };
            tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                wordWrap = false,
                normal = { textColor = new Color(1f, 0.92f, 0.72f) }
            };
        }

        private bool DrawIcon(Rect rect, PotcoItemDefinition definition)
        {
            if (definition == null || guiResolver == null)
                return false;

            PotcoGuiRegion icon = guiResolver.ResolveItemIcon(definition);
            if (icon.Texture == null)
                return false;

            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(rect, icon.Texture, icon.TexCoords, true);
            GUI.color = old;
            return true;
        }

        private void DrawSkillStrip(Rect screenRect)
        {
            if (weaponController.CurrentWeapon == null || weaponController.CurrentWeapon.Skills.Count == 0)
                return;

            int skillCount = Mathf.Min(9, weaponController.CurrentWeapon.Skills.Count);
            Rect[] skillRects = BuildSkillRects(screenRect, skillCount, PotcoInventoryLocations.EquipWeapons.Count);
            for (int i = 0; i < skillRects.Length; i++)
            {
                PotcoWeaponSkill skill = weaponController.CurrentWeapon.Skills[i];
                Rect rect = skillRects[i];
                string hotkey = (i + 1).ToString();
                string tooltip = $"{hotkey}: {skill.Name}  {skill.IconName}";
                GUI.Box(rect, new GUIContent(string.Empty, tooltip), skillSlotStyle);

                GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, 14f, 14f), hotkey, labelStyle);

                Rect iconRect = new Rect(rect.x + 6f, rect.y + 12f, rect.width - 12f, rect.height - 16f);
                if (!DrawSkillIcon(iconRect, skill))
                    GUI.Label(new Rect(rect.x + 3f, rect.y + 14f, rect.width - 6f, rect.height - 17f), skill.Name, labelStyle);

                if (!weaponController.IsWeaponDrawn)
                    DrawDisabledOverlay(iconRect);
                else
                    DrawSkillCooldownOverlay(iconRect, skill);
            }
        }

        private bool DrawSkillIcon(Rect rect, PotcoWeaponSkill skill)
        {
            if (skill == null || guiResolver == null)
                return false;

            bool drewBase = DrawRegion(rect, guiResolver.ResolveRegion(PotcoRuntimeGuiAssetResolver.SkillIconsGui, "base"));
            PotcoGuiRegion icon = guiResolver.ResolveSkillIcon(skill.IconName);
            if (!icon.IsDefined)
                return drewBase;

            Rect iconRect = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
            DrawRegion(iconRect, icon);
            return true;
        }

        private static bool DrawRegion(Rect rect, PotcoGuiRegion region)
        {
            if (region == null || region.Texture == null)
                return false;

            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(rect, region.Texture, region.TexCoords, true);
            GUI.color = old;
            return true;
        }

        private static void DrawRarityAccent(Rect rect, PotcoInventoryItemStack stack)
        {
            if (stack == null)
                return;

            Color old = GUI.color;
            GUI.color = GetRarityColor(stack.Definition.Rarity);
            GUI.DrawTexture(new Rect(rect.x + 4f, rect.yMax - 5f, rect.width - 8f, 3f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawCooldownOverlay(Rect rect, bool selected)
        {
            if (!selected || weaponController == null || weaponController.CurrentWeapon == null || weaponController.CurrentWeapon.Skills.Count == 0)
                return;

            float ratio = weaponController.GetSkillCooldownRatio(weaponController.CurrentWeapon.Skills[0]);
            if (ratio <= 0f)
                return;

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height * (1f - ratio), rect.width, rect.height * ratio), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawSkillCooldownOverlay(Rect rect, PotcoWeaponSkill skill)
        {
            float ratio = weaponController.GetSkillCooldownRatio(skill);
            if (ratio <= 0f)
                return;

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height * (1f - ratio), rect.width, rect.height * ratio), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void DrawDisabledOverlay(Rect rect)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static Color GetRarityColor(int rarity)
        {
            switch (rarity)
            {
                case 1:
                    return new Color(0.58f, 0.39f, 0.19f);
                case 2:
                    return new Color(0.82f, 0.68f, 0.24f);
                case 3:
                    return new Color(0.24f, 0.84f, 0.25f);
                case 4:
                    return new Color(0.38f, 0.54f, 0.93f);
                case 5:
                    return new Color(0.94f, 0.62f, 0.20f);
                default:
                    return new Color(0.24f, 0.84f, 0.25f);
            }
        }
    }
}
