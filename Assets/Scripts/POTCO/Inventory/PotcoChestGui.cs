using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO.Inventory
{
    [DisallowMultipleComponent]
    public sealed class PotcoChestGui : MonoBehaviour
    {
        private const string GuiAlphaShaderResource = "Shaders/PotcoGuiTextureWithAlpha";
        private const float ReferenceFrameMinX = -0.55f;
        private const float ReferenceFrameMaxX = 0.55f;
        private const float ReferenceFrameMinZ = -0.82f;
        private const float ReferenceFrameMaxZ = 0.72f;
        private const float ReferenceButtonSize = 0.14f;
        private const float ReferenceSideTabWidth = 0.25f;
        private const float ReferenceSideTabInactiveHeight = 0.2f;
        private const float ReferenceSideTabActiveHeight = 0.22f;
        private const float ReferenceSideTabSpacing = 0.132f;
        private const float ReferenceChestTabFrameWidth = 0.25f;
        private const float ReferenceChestTabFrameHeight = 0.2f;
        private const float ReferenceChestTabBorderScale = 0.38f;
        private const float ReferenceChestTabCornerWidth = 0.15f;
        private const float ReferenceGeneralFrameEdgeThickness = 0.15f;
        private const float ReferenceTopLevelIconLocalExtent = 0.533334f;
        private const float ReferenceChestSlideSeconds = 0.2f;
        private const float ReferenceChestHiddenZ = -1.8f;
        private const float ReferenceChestVisibleZ = 0.8f;
        private const float ReferenceChestTraySlideOffset = 0.2f;
        private const float ReferenceChestTrayButtonSize = 0.12f;
        private const float ReferenceChestTrayButtonSpacing = 0.12f;
        private static readonly Vector2 ReferenceChestTrayIconPosition = new Vector2(0.06f, 0.06f);
        private const float ReferenceInventoryBackingScale = 0.335f;
        private const float ReferenceTitleBarScale = 0.2f;
        private const float ReferenceSeaChestScale = 0.32f;
        private static readonly Vector2 ReferenceSeaChestBackgroundPosition = new Vector2(-0.0014f, 0.0793f);
        private static readonly Vector2 ReferenceSeaChestSideTentaclePosition = new Vector2(0.5945f, 0.2652f);
        private static readonly Vector2 ReferenceSeaChestBorderPosition = new Vector2(0.0623f, -0.0166f);
        private const float ReferenceCharGuiButtonScale = 0.45f;
        private const float ReferenceCharGuiButtonWidth = 0.625f * ReferenceCharGuiButtonScale;
        private const float ReferenceCharGuiButtonHeight = 0.225f * ReferenceCharGuiButtonScale;
        private const float ReferenceTrashGeomScale = 0.4f;
        private const float ReferenceGoldItemImageScale = 0.378f;
        private static readonly bool UseNativeChestChrome = false;
        private static readonly bool UseNativeTrayChest = false;
        private static readonly bool UseNativeTrayButtonBoxes = false;
        private const string ReferenceChestTrayBoxGroup = "topgui_icon_box";
        private const string ReferenceChestTrayActiveBoxGroup = "topgui_icon_box_in";
        private static readonly Vector2 ReferenceSingleCellOffset = new Vector2(ReferenceButtonSize * 0.5f, ReferenceButtonSize * 0.5f);
        private static readonly Vector2 ReferenceTitleBarImagePosition = new Vector2(0f, 0.345f);
        private static readonly Vector2 ReferenceInventoryPagePosition = new Vector2(-0.54f, -0.72f);
        private static readonly Vector2 ReferenceWeaponBagPosition = new Vector2(0.12f, 0.20f);
        private static readonly Vector2 ReferenceClothingBagPosition = new Vector2(0.31f, 0.20f);
        private static readonly Vector2 ReferenceJewelryBagPosition = new Vector2(0.46f, 0.21f);
        private static readonly Vector2 ReferencePotionBagPosition = new Vector2(0.12f, 0.22f);
        private static readonly Vector2 ReferenceAmmoBagPosition = new Vector2(0.03f, 0.248f);
        private static readonly Vector2 ReferenceCardsBagPosition = new Vector2(0.16f, 0.20f);
        private static readonly Vector2 ReferenceHotbarPosition = new Vector2(0.075f, 0.895f);
        private static readonly Vector2 ReferenceClothingDressingPosition = new Vector2(-0.225f, 0f);
        private static readonly Vector2 ReferenceJewelryDressingPosition = new Vector2(-0.37f, 0.41f);
        private static readonly Vector2 ReferenceTattooDressingPosition = new Vector2(-0.37f, 0f);
        private static readonly Color ReferenceEquipCellTint = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color ReferenceEquipIconTint = new Color(0.4f, 0.4f, 0.4f, 1f);
        private static readonly Color ReferenceChestTrayNormalBoxTint = new Color(0.7f, 0.7f, 0.7f, 1f);
        private static readonly Color ReferenceChestTrayHoverBoxTint = new Color(0.8f, 0.8f, 0.8f, 1f);
        private static readonly ReferenceTrayButton[] ReferenceChestTrayButtons =
        {
            new ReferenceTrayButton("guiMgrToggleSocial", PotcoRuntimeGuiAssetResolver.TopLevelGui, "friend_button_over", 0.12f, 0.72f, "F"),
            new ReferenceTrayButton("guiMgrToggleRadar", PotcoRuntimeGuiAssetResolver.TopLevelGui, "compass_small_button_open_over", 0.09f, 0.90f, "C"),
            new ReferenceTrayButton("guiMgrToggleMap", PotcoRuntimeGuiAssetResolver.MainGui, "world_map_icon", 0.095f, 0.90f, "M"),
            new ReferenceTrayButton("guiMgrToggleInventory", PotcoRuntimeGuiAssetResolver.TopLevelGui, "treasure_chest_closed_over", 0.12f, 0.90f, "I"),
            new ReferenceTrayButton("guiMgrToggleWeapons", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_icon_weapons", 0.18f, ReferenceTopLevelIconLocalExtent, "Y"),
            new ReferenceTrayButton("guiMgrToggleLevels", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_icon_skills", 0.18f, ReferenceTopLevelIconLocalExtent, "K"),
            new ReferenceTrayButton("guiMgrToggleTitles", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_infamy_frame", 0.20f, ReferenceTopLevelIconLocalExtent, "B"),
            new ReferenceTrayButton("guiMgrToggleShips", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_icon_ship", 0.20f, ReferenceTopLevelIconLocalExtent, "H"),
            new ReferenceTrayButton("guiMgrToggleQuest", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_icon_journal", 0.18f, ReferenceTopLevelIconLocalExtent, "J"),
            new ReferenceTrayButton("guiMgrToggleLookout", PotcoRuntimeGuiAssetResolver.TopLevelGui, "telescope_button_over", 0.30f, 0.44f, "L"),
            new ReferenceTrayButton("guiMgrToggleMainMenu", PotcoRuntimeGuiAssetResolver.TopLevelGui, "topgui_icon_main_menu", 0.18f, ReferenceTopLevelIconLocalExtent, "F7")
        };
        private static readonly ReferenceEquipSlot[] ClothingDressingSlots =
        {
            new ReferenceEquipSlot(50, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_hat", ReferenceButtonSize * 0.70f, false),
            new ReferenceEquipSlot(51, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_coat", ReferenceButtonSize * 0.70f, false),
            new ReferenceEquipSlot(52, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_vest", ReferenceButtonSize * 0.60f, false),
            new ReferenceEquipSlot(53, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_cloth", ReferenceButtonSize * 1.40f, false),
            new ReferenceEquipSlot(54, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_belt", ReferenceButtonSize * 0.70f, false),
            new ReferenceEquipSlot(55, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_pants", ReferenceButtonSize * 0.70f, false),
            new ReferenceEquipSlot(56, PotcoRuntimeGuiAssetResolver.TailorIconsGui, "icon_shop_tailor_booths", ReferenceButtonSize * 0.70f, false)
        };
        private static readonly ReferenceEquipSlot[] JewelryDressingSlots =
        {
            new ReferenceEquipSlot(100, PotcoRuntimeGuiAssetResolver.ShopIconsGui, "icon_shop_tailor_brow", ReferenceButtonSize * 0.60f, true),
            new ReferenceEquipSlot(101, PotcoRuntimeGuiAssetResolver.ShopIconsGui, "icon_shop_tailor_brow", ReferenceButtonSize * 0.60f, false),
            new ReferenceEquipSlot(102, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_ears", ReferenceButtonSize * 1.80f, true),
            new ReferenceEquipSlot(103, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_ears", ReferenceButtonSize * 1.80f, false),
            new ReferenceEquipSlot(104, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_nose", ReferenceButtonSize * 1.80f, false),
            new ReferenceEquipSlot(105, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_mouth", ReferenceButtonSize * 1.40f, false),
            new ReferenceEquipSlot(106, PotcoRuntimeGuiAssetResolver.ShopIconsGui, "icon_shop_jeweler_ring", ReferenceButtonSize * 0.80f, true),
            new ReferenceEquipSlot(107, PotcoRuntimeGuiAssetResolver.ShopIconsGui, "icon_shop_jeweler_ring", ReferenceButtonSize * 0.80f, false)
        };
        private static readonly ReferenceEquipSlot[] TattooDressingSlots =
        {
            new ReferenceEquipSlot(110, PotcoRuntimeGuiAssetResolver.TattooIconsGui, "icon_shop_tailor_arm", ReferenceButtonSize * 0.60f, true),
            new ReferenceEquipSlot(111, PotcoRuntimeGuiAssetResolver.TattooIconsGui, "icon_shop_tailor_arm", ReferenceButtonSize * 0.60f, false),
            new ReferenceEquipSlot(112, PotcoRuntimeGuiAssetResolver.TattooIconsGui, "icon_shop_tailor_chest_male", ReferenceButtonSize * 0.60f, false),
            new ReferenceEquipSlot(113, PotcoRuntimeGuiAssetResolver.TattooIconsGui, "icon_shop_tailor_face_male", ReferenceButtonSize * 0.60f, false)
        };

        private PotcoInventoryController controller;
        private PotcoChestLayout layout;
        private PotcoRuntimeGuiAssetResolver resolver;
        private PotcoChestTextureSkin textureSkin;
        private PotcoChestNativeGuiLayer nativeGuiLayer;
        private readonly Dictionary<string, Material> guiAlphaMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

        private bool open;
        private bool nativeGuiReady;
        private bool showDebugAddControls;
        private float chestOpenProgress;
        private PotcoChestPageKind activePage = PotcoChestPageKind.WeaponBelt;
        private int selectedLocation = PotcoInventoryLocations.InvalidLocation;
        private string addItemId = "1";
        private string addQuantity = "1";
        private readonly HashSet<string> nativeDrawnKeys = new HashSet<string>(StringComparer.Ordinal);

        private GUIStyle titleStyle;
        private GUIStyle tabStyle;
        private GUIStyle activeTabStyle;
        private GUIStyle slotStyle;
        private GUIStyle selectedSlotStyle;
        private GUIStyle labelStyle;
        private GUIStyle tooltipStyle;
        private GUIStyle buttonTextStyle;
        private GUIStyle inputStyle;

        private void Awake()
        {
            controller = GetComponent<PotcoInventoryController>();
            if (controller == null)
                controller = gameObject.AddComponent<PotcoInventoryController>();

            layout = PotcoChestLayout.CreateDefault();
            resolver = new PotcoRuntimeGuiAssetResolver();
            textureSkin = PotcoChestTextureSkin.CreateDefault(resolver);
            nativeGuiLayer = GetComponent<PotcoChestNativeGuiLayer>();
            if (nativeGuiLayer == null)
                nativeGuiLayer = gameObject.AddComponent<PotcoChestNativeGuiLayer>();
        }

        private void OnDestroy()
        {
            foreach (Material material in guiAlphaMaterials.Values)
            {
                if (material != null)
                    Destroy(material);
            }

            guiAlphaMaterials.Clear();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                open = !open;
            if (Input.GetKeyDown(KeyCode.F9))
                showDebugAddControls = !showDebugAddControls;

            float targetProgress = open ? 1f : 0f;
            chestOpenProgress = Mathf.MoveTowards(chestOpenProgress, targetProgress, Time.unscaledDeltaTime / ReferenceChestSlideSeconds);
        }

        private void LateUpdate()
        {
            PrepareNativeGuiMeshes();
        }

        public void SetOpen(bool value)
        {
            open = value;
        }

        private bool IsChestRenderable => open || chestOpenProgress > 0.001f;

        private void PrepareNativeGuiMeshes()
        {
            nativeDrawnKeys.Clear();
            if (!UseNativeTrayChest && !UseNativeTrayButtonBoxes && !UseNativeChestChrome)
            {
                nativeGuiReady = false;
                if (nativeGuiLayer != null)
                    nativeGuiLayer.SetVisible(false);
                return;
            }

            nativeGuiReady = nativeGuiLayer != null && nativeGuiLayer.BeginGuiFrame(true);
            if (!nativeGuiReady)
                return;

            Rect trayRect = CalculateTrayRect();
            Vector2 guiMouse = GetGuiMousePosition();
            bool trayHover = trayRect.Contains(guiMouse);
            if (UseNativeTrayChest && nativeGuiLayer.ShowTrayChest(trayRect.Contract(2f), open, trayHover))
                nativeDrawnKeys.Add("tray.chest");

            if (!IsChestRenderable)
                return;

            if (UseNativeTrayButtonBoxes)
                PrepareNativeChestTrayButtons(CalculatePanelRect());

            if (!controller.EnsureLoaded())
                return;

            Rect panel = CalculateAnimatedPanelRect();
            Rect titleRect = CalculateTitleRect(panel);
            Rect pageBacking = CalculatePageBackingRect(CalculateContentRect(panel));
            IReadOnlyList<Rect> sideTabRects = CalculateSideTabRects(panel);
            if (UseNativeChestChrome && nativeGuiLayer.ApplyChestPanel(panel, titleRect, pageBacking, sideTabRects, activePage))
                nativeDrawnKeys.Add("panel");
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawTray();

            if (!IsChestRenderable)
                return;

            if (!controller.EnsureLoaded())
            {
                DrawLoadError();
                return;
            }

            DrawChestPanel();
        }

        private void DrawTray()
        {
            if (IsChestRenderable)
                DrawReferenceChestTrayButtons(CalculatePanelRect());

            Rect rect = CalculateTrayRect();
            float x = rect.x;
            float y = rect.y;

            Event current = Event.current;
            bool hover = current != null && rect.Contains(current.mousePosition);
            string chestSprite = GetTrayChestSpriteName(open, hover);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                open = !open;

            if (!nativeDrawnKeys.Contains("tray.chest"))
            {
                if (!DrawTextureSprite(rect.Contract(2f), chestSprite, Color.white))
                    DrawRegion(rect.Contract(5f), resolver.ResolveLooseTexture(GetTrayChestTextureName(open, hover)), Color.white);
            }
            GUI.Label(new Rect(x - 1f, y + rect.height - 14f, rect.width + 2f, 18f), "Tab", labelStyle);
        }

        private void DrawReferenceChestTrayButtons(Rect finalPanel)
        {
            if (chestOpenProgress <= 0.001f)
                return;

            for (int i = 0; i < ReferenceChestTrayButtons.Length; i++)
            {
                ReferenceTrayButton button = ReferenceChestTrayButtons[i];
                Rect rect = CalculateReferenceChestTrayButtonRect(finalPanel, i);
                bool active = button.Command == "guiMgrToggleInventory";
                DrawReferenceChestTrayButton(rect, button, active, i);
            }
        }

        private void PrepareNativeChestTrayButtons(Rect finalPanel)
        {
            if (chestOpenProgress <= 0.001f || nativeGuiLayer == null)
                return;

            Vector2 guiMouse = GetGuiMousePosition();
            for (int i = 0; i < ReferenceChestTrayButtons.Length; i++)
            {
                ReferenceTrayButton button = ReferenceChestTrayButtons[i];
                Rect rect = CalculateReferenceChestTrayButtonRect(finalPanel, i);
                bool active = button.Command == "guiMgrToggleInventory";
                bool hover = rect.Contains(guiMouse);
                string boxKey = GetReferenceChestTrayBoxKey(i);

                if (nativeGuiLayer.ShowTopLevelIconBox(rect, active || hover, boxKey))
                    nativeDrawnKeys.Add(boxKey);
            }
        }

        private Rect CalculateReferenceChestTrayButtonRect(Rect finalPanel, int index)
        {
            GuiReferenceLayout reference = CalculateReferenceLayout(finalPanel);
            float unitScale = reference.UnitScale;
            float buttonSize = ReferenceChestTrayButtonSize * unitScale;
            float spacing = ReferenceChestTrayButtonSpacing * unitScale;
            float easedProgress = Mathf.SmoothStep(0f, 1f, chestOpenProgress);
            float hiddenOffset = ReferenceChestTraySlideOffset * unitScale * (1f - easedProgress);
            float x = Mathf.Min(Screen.width - buttonSize - 5f, finalPanel.xMax + buttonSize * 0.16f) + hiddenOffset;
            Rect firstButtonReferenceRect = reference.RectFromCenter(new Vector2(0.68f, 0.42f), ReferenceChestTrayButtonSize, ReferenceChestTrayButtonSize);
            float y = Mathf.Max(8f, firstButtonReferenceRect.y);
            return new Rect(x, y + spacing * index, buttonSize, buttonSize);
        }

        private static string GetReferenceChestTrayBoxKey(int index)
        {
            return "tray.button." + index + ".box";
        }

        private void DrawReferenceChestTrayButton(Rect rect, ReferenceTrayButton button, bool active, int index)
        {
            Event current = Event.current;
            bool hover = current != null && rect.Contains(current.mousePosition);
            Color iconTint = Color.white;
            Color boxTint = hover ? ReferenceChestTrayHoverBoxTint : ReferenceChestTrayNormalBoxTint;
            string boxGroup = active || hover ? ReferenceChestTrayActiveBoxGroup : ReferenceChestTrayBoxGroup;
            string boxSprite = active || hover ? PotcoChestTextureSkin.TrayIconBoxOver : PotcoChestTextureSkin.TrayIconBox;

            if (!DrawGuiSprite(rect, PotcoRuntimeGuiAssetResolver.TopLevelGui, boxGroup, boxTint) &&
                !DrawTextureSprite(rect, boxSprite, boxTint))
            {
                PotcoGuiRegion boxRegion = resolver.ResolveRegion(
                    PotcoRuntimeGuiAssetResolver.TopLevelGui,
                    boxGroup);
                if (!DrawRegion(rect, boxRegion, boxTint))
                    DrawFilledRect(rect, active ? new Color(0.62f, 0.48f, 0.08f, 0.9f) : new Color(0.38f, 0.36f, 0.28f, 0.88f));
            }

            DrawReferenceChestTrayIcon(rect, button, iconTint);

            GUI.Label(new Rect(rect.x + rect.width * 0.52f, rect.y + rect.height * 0.56f, rect.width * 0.54f, rect.height * 0.36f), button.HotkeyLabel, labelStyle);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && button.Command == "guiMgrToggleInventory")
                open = !open;
        }

        private void DrawReferenceChestTrayIcon(Rect buttonRect, ReferenceTrayButton button, Color iconTint)
        {
            PotcoGuiSprite sprite = resolver.ResolveSprite(button.ModelResourcePath, button.GroupName);
            if (sprite != null && sprite.IsDefined)
            {
                Rect iconRect = RectFromButtonLocalBounds(
                    buttonRect,
                    ReferenceChestTrayIconPosition,
                    Vector2.one * button.GeomScale,
                    sprite.LocalBounds);
                if (DrawGuiSprite(iconRect, sprite, iconTint, false, true))
                    return;
            }

            Rect fallbackBounds = new Rect(-button.LocalExtent * 0.5f, -button.LocalExtent * 0.5f, button.LocalExtent, button.LocalExtent);
            Rect fallbackRect = RectFromButtonLocalBounds(
                buttonRect,
                ReferenceChestTrayIconPosition,
                Vector2.one * button.GeomScale,
                fallbackBounds);
            if (DrawRegion(fallbackRect, resolver.ResolveRegion(button.ModelResourcePath, button.GroupName), iconTint, false, true))
                return;

            DrawRegion(buttonRect.Contract(4f), resolver.ResolveLooseTexture(button.GroupName), iconTint);
        }

        private static Rect CalculateTrayRect()
        {
            const float buttonSize = 54f;
            return new Rect(Screen.width - buttonSize - 18f, Screen.height - buttonSize - 18f, buttonSize, buttonSize);
        }

        private static Vector2 GetGuiMousePosition()
        {
            Vector3 mouse = Input.mousePosition;
            return new Vector2(mouse.x, Screen.height - mouse.y);
        }

        private void DrawLoadError()
        {
            Rect panel = new Rect(Screen.width - 460f, Screen.height - 220f, 420f, 130f);
            GUI.Box(panel, "POTCO Inventory");
            GUI.Label(panel.Contract(16f), controller.LoadError, tooltipStyle);
        }

        private void DrawChestPanel()
        {
            LastSlotRects.Clear();

            Rect panel = CalculateAnimatedPanelRect();
            GuiReferenceLayout reference = CalculateReferenceLayout(panel);
            PotcoChestPageLayout page = layout.GetPage(activePage);
            Rect titleRect = CalculateTitleRect(panel);

            Rect content = CalculateContentRect(panel);
            Rect pageBacking = CalculatePageBackingRect(content);
            IReadOnlyList<Rect> sideTabRects = CalculateSideTabRects(panel);
            DrawReferenceChestSideTentacle(reference);
            DrawInactiveSideTabs(sideTabRects);

            bool drewReferenceBackground = DrawReferenceChestBackground(reference);
            if (!drewReferenceBackground && !nativeDrawnKeys.Contains("panel"))
                DrawFallbackPanel(panel, titleRect, pageBacking);
            DrawReferencePageBacking(reference, page);

            if (activePage == PotcoChestPageKind.WeaponBelt)
                DrawHotbar(reference);

            if (activePage == PotcoChestPageKind.Treasure)
                DrawTreasure(reference);
            else
                DrawGrid(reference, page);

            DrawReferenceEquipmentSlots(reference);

            if (ShouldShowBottomControls(activePage))
                DrawBottomControls(reference);
            DrawReferenceChestBorderFrame(reference);
            DrawActiveSideTab(sideTabRects);
            DrawReferenceTitleBar(reference, titleRect);
            DrawPanelTitle(titleRect, page);
            DrawSideTabHitboxes(sideTabRects);
            DrawTooltipForMouse(panel);
        }

        private bool DrawReferenceChestChrome(GuiReferenceLayout reference)
        {
            bool drewBackground = DrawReferenceChestBackground(reference);
            bool drewBorder = DrawReferenceChestBorder(reference);
            return drewBackground && drewBorder;
        }

        private bool DrawReferenceChestBackground(GuiReferenceLayout reference)
        {
            bool drewBackground = DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.SeaChestGui, "background", ReferenceSeaChestBackgroundPosition, Vector2.one * ReferenceSeaChestScale, Color.white, false, true);
            return drewBackground;
        }

        private bool DrawReferenceChestBorder(GuiReferenceLayout reference)
        {
            bool drewSideTentacle = DrawReferenceChestSideTentacle(reference);
            bool drewBorder = DrawReferenceChestBorderFrame(reference);
            return drewSideTentacle && drewBorder;
        }

        private bool DrawReferenceChestSideTentacle(GuiReferenceLayout reference)
        {
            return DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.SeaChestGui, "side_tentacle", ReferenceSeaChestSideTentaclePosition, Vector2.one * ReferenceSeaChestScale, Color.white, false, true);
        }

        private bool DrawReferenceChestBorderFrame(GuiReferenceLayout reference)
        {
            bool drewBorder = DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.SeaChestGui, "border", ReferenceSeaChestBorderPosition, Vector2.one * ReferenceSeaChestScale, Color.white, false, true);
            return drewBorder;
        }

        private static Rect CalculatePanelRect()
        {
            float referenceWidth = ReferenceFrameMaxX - ReferenceFrameMinX;
            float referenceHeight = ReferenceFrameMaxZ - ReferenceFrameMinZ;
            float maxWidth = Mathf.Min(660f, Screen.width - 118f);
            float maxHeight = Mathf.Min(760f, Screen.height - 42f);
            float unitScale = Mathf.Min(maxWidth / referenceWidth, maxHeight / referenceHeight);
            float width = referenceWidth * unitScale;
            float height = referenceHeight * unitScale;
            Rect panel = new Rect(Screen.width - width - 80f, Screen.height - height - 24f, width, height);
            if (panel.x < 14f)
                panel.x = 14f;
            if (panel.y < 12f)
                panel.y = 12f;

            return panel;
        }

        private Rect CalculateAnimatedPanelRect()
        {
            Rect panel = CalculatePanelRect();
            float easedProgress = Mathf.SmoothStep(0f, 1f, chestOpenProgress);
            float currentZ = Mathf.Lerp(ReferenceChestHiddenZ, ReferenceChestVisibleZ, easedProgress);
            float unitScale = CalculateReferenceLayout(panel).UnitScale;
            panel.y += (ReferenceChestVisibleZ - currentZ) * unitScale;
            return panel;
        }

        private static Rect CalculateTitleRect(Rect panel)
        {
            return CalculateReferenceLayout(panel).RectFromCenter(new Vector2(0f, 0.66f), 0.62f, 0.12f);
        }

        private static Rect CalculateContentRect(Rect panel)
        {
            return CalculateReferenceLayout(panel).RectFromBounds(-0.42f, 0.42f, -0.50f, 0.16f);
        }

        private static GuiReferenceLayout CalculateReferenceLayout(Rect panel)
        {
            return GuiReferenceLayout.FromPanel(panel);
        }

        private static Rect CalculatePanelBackgroundRect(Rect panel)
        {
            return new Rect(panel.x + 16f, panel.y + 50f, panel.width - 34f, panel.height - 68f);
        }

        private static Rect CalculatePanelBorderRect(Rect panel)
        {
            return new Rect(panel.x - 6f, panel.y + 4f, panel.width + 14f, panel.height - 2f);
        }

        private static Rect CalculateSideTentacleRect(Rect panel)
        {
            return new Rect(panel.x - 58f, panel.y + 154f, panel.width * 0.16f, panel.height * 0.76f);
        }

        private static Rect CalculatePageBackingRect(Rect content)
        {
            return new Rect(content.x - 30f, content.y - 16f, content.width + 60f, content.height + 62f);
        }

        private Rect CalculateSideTabRect(Rect panel, int index)
        {
            IReadOnlyList<PotcoChestPageLayout> pages = GetOrderedPages();
            bool active = index >= 0 && index < pages.Count && pages[index].Kind == activePage;
            float centerX = active ? -0.64f : -0.62f;
            float height = active ? ReferenceSideTabActiveHeight : ReferenceSideTabInactiveHeight;
            return CalculateReferenceLayout(panel).RectFromCenter(new Vector2(centerX, 0.37f - index * ReferenceSideTabSpacing), ReferenceSideTabWidth, height);
        }

        private IReadOnlyList<Rect> CalculateSideTabRects(Rect panel)
        {
            IReadOnlyList<PotcoChestPageLayout> pages = GetOrderedPages();
            Rect[] rects = new Rect[pages.Count];
            for (int i = 0; i < rects.Length; i++)
                rects[i] = CalculateSideTabRect(panel, i);

            return rects;
        }

        private static bool ShouldShowBottomControls(PotcoChestPageKind pageKind)
        {
            return pageKind == PotcoChestPageKind.WeaponBelt ||
                   pageKind == PotcoChestPageKind.Garb ||
                   pageKind == PotcoChestPageKind.JewelryAndTattoos ||
                   pageKind == PotcoChestPageKind.PotionsPouch;
        }

        private void DrawFallbackPanel(Rect panel, Rect titleRect, Rect pageBacking)
        {
            DrawFilledRect(CalculatePanelBackgroundRect(panel), new Color(0.26f, 0.02f, 0.02f, 0.96f));
            DrawFilledRect(pageBacking, new Color(0.31f, 0.01f, 0.01f, 0.82f));
            DrawFilledRect(CalculatePanelBorderRect(panel), new Color(0.08f, 0.07f, 0.05f, 0.72f));
            DrawFilledRect(titleRect, new Color(0.16f, 0.08f, 0.05f, 0.96f));
        }

        private void DrawReferencePageBacking(GuiReferenceLayout reference, PotcoChestPageLayout page)
        {
            string groupName = GetReferencePageBackingGroup(page.Kind);
            Vector2 referencePosition = GetReferencePageBackingPosition(page.Kind);
            if (DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.MainGui, groupName, referencePosition, Vector2.one * ReferenceInventoryBackingScale, Color.white))
                return;

            Rect fallback = reference.RectFromBounds(-0.46f, 0.46f, -0.66f, 0.28f);
            DrawFilledRect(fallback, new Color(0.34f, 0.015f, 0.015f, 0.88f));
        }

        private static string GetReferencePageBackingGroup(PotcoChestPageKind pageKind)
        {
            switch (pageKind)
            {
                case PotcoChestPageKind.Garb:
                    return "gui_inv_clothing";
                case PotcoChestPageKind.JewelryAndTattoos:
                    return "gui_inv_jewelry";
                case PotcoChestPageKind.PotionsPouch:
                    return "gui_inv_red_general1";
                case PotcoChestPageKind.Ammo:
                case PotcoChestPageKind.Materials:
                    return "gui_inv_ammo";
                case PotcoChestPageKind.Cards:
                    return "gui_inv_cards";
                case PotcoChestPageKind.WeaponBelt:
                case PotcoChestPageKind.Treasure:
                default:
                    return "gui_inv_weapon";
            }
        }

        private static Vector2 GetReferencePageBackingPosition(PotcoChestPageKind pageKind)
        {
            switch (pageKind)
            {
                case PotcoChestPageKind.PotionsPouch:
                    return new Vector2(0f, 0.02f);
                case PotcoChestPageKind.Ammo:
                case PotcoChestPageKind.Materials:
                    return new Vector2(0f, -0.05f);
                case PotcoChestPageKind.Cards:
                    return new Vector2(-0.07f, -0.05f);
                default:
                    return Vector2.zero;
            }
        }

        private void DrawReferenceTitleBar(GuiReferenceLayout reference, Rect fallbackRect)
        {
            if (DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.MainGui, "title_bar_08", ReferenceTitleBarImagePosition, Vector2.one * ReferenceTitleBarScale, Color.white, false, true))
                return;

            DrawFilledRect(fallbackRect, new Color(0.16f, 0.08f, 0.05f, 0.96f));
        }

        private void DrawPanelTitle(Rect titleRect, PotcoChestPageLayout page)
        {
            GUI.Label(new Rect(titleRect.x + 10f, titleRect.y + 3f, titleRect.width - 20f, titleRect.height - 8f), page.Title, titleStyle);
        }

        private void DrawInactiveSideTabs(IReadOnlyList<Rect> sideTabRects)
        {
            IReadOnlyList<PotcoChestPageLayout> pages = GetOrderedPages();
            for (int i = pages.Count - 1; i >= 0; i--)
            {
                if (i >= sideTabRects.Count)
                    continue;

                PotcoChestPageLayout page = pages[i];
                bool active = activePage == page.Kind;
                if (active)
                    continue;

                DrawSideTabArt(page, sideTabRects[i], false);
            }
        }

        private void DrawActiveSideTab(IReadOnlyList<Rect> sideTabRects)
        {
            IReadOnlyList<PotcoChestPageLayout> pages = GetOrderedPages();
            for (int i = 0; i < pages.Count && i < sideTabRects.Count; i++)
            {
                PotcoChestPageLayout page = pages[i];
                if (activePage != page.Kind)
                    continue;

                DrawSideTabArt(page, sideTabRects[i], true);
                return;
            }
        }

        private void DrawSideTabHitboxes(IReadOnlyList<Rect> sideTabRects)
        {
            IReadOnlyList<PotcoChestPageLayout> pages = GetOrderedPages();
            Event current = Event.current;
            for (int i = 0; i < pages.Count && i < sideTabRects.Count; i++)
            {
                PotcoChestPageLayout page = pages[i];
                Rect tabRect = sideTabRects[i];
                bool hover = current != null && tabRect.Contains(current.mousePosition);

                if (GUI.Button(tabRect, GUIContent.none, GUIStyle.none))
                    activePage = page.Kind;

                if (hover)
                    GUI.Label(new Rect(tabRect.xMax + 4f, tabRect.y + 2f, 170f, tabRect.height - 4f), page.Title, labelStyle);
            }
        }

        private void DrawSideTabArt(PotcoChestPageLayout page, Rect tabRect, bool active)
        {
            DrawReferenceChestTabFrame(tabRect, active);

            if (!DrawReferencePageIcon(page, tabRect, active, active ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f)))
                GUI.Label(tabRect, page.Title.Substring(0, Math.Min(2, page.Title.Length)), active ? activeTabStyle : tabStyle);
        }

        private void DrawReferenceChestTabFrame(Rect tabRect, bool emphasized)
        {
            if (tabRect.width <= 0f || tabRect.height <= 0f)
                return;

            float borderWidth = Mathf.Clamp(
                tabRect.width * (ReferenceChestTabBorderScale * ReferenceGeneralFrameEdgeThickness / ReferenceChestTabFrameWidth),
                4f,
                tabRect.width * 0.35f);
            float borderHeight = Mathf.Clamp(
                tabRect.height * (ReferenceChestTabBorderScale * ReferenceGeneralFrameEdgeThickness / ReferenceChestTabFrameHeight),
                4f,
                tabRect.height * 0.35f);
            float cornerWidth = Mathf.Clamp(
                tabRect.width * (ReferenceChestTabCornerWidth * ReferenceGeneralFrameEdgeThickness / ReferenceChestTabFrameWidth),
                5f,
                tabRect.width * 0.22f);
            float cornerHeight = Mathf.Clamp(
                tabRect.height * (ReferenceChestTabCornerWidth * ReferenceGeneralFrameEdgeThickness / ReferenceChestTabFrameHeight),
                5f,
                tabRect.height * 0.22f);
            Color tint = Color.white;
            DrawReferenceChestTabBackground(tabRect, borderWidth, borderHeight);

            bool drew =
                DrawRegion(new Rect(tabRect.x, tabRect.y, tabRect.width, borderHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "top1"), tint, false) |
                DrawRegion(new Rect(tabRect.x, tabRect.yMax - borderHeight, tabRect.width, borderHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "bottom"), tint, false) |
                DrawRegion(new Rect(tabRect.x, tabRect.y, borderWidth, tabRect.height), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "left"), tint, false) |
                DrawReferenceLeftTabRightEdge(tabRect, borderWidth) |
                DrawRegion(new Rect(tabRect.x, tabRect.y, cornerWidth, cornerHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "topLeft"), tint, false) |
                DrawRegion(new Rect(tabRect.xMax - cornerWidth, tabRect.y, cornerWidth, cornerHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "topRight"), tint, false) |
                DrawRegion(new Rect(tabRect.x, tabRect.yMax - cornerHeight, cornerWidth, cornerHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "bottomLeft"), tint, false) |
                DrawRegion(new Rect(tabRect.xMax - cornerWidth, tabRect.yMax - cornerHeight, cornerWidth, cornerHeight), resolver.ResolveRegion(PotcoChestNativeGuiLayer.GeneralFrameDGui, "bottomRight"), tint, false);

            if (!drew)
            {
                Color outer = emphasized ? new Color(0.82f, 0.79f, 0.62f, 1f) : new Color(0.52f, 0.50f, 0.40f, 1f);
                DrawReferenceChestTabBackground(tabRect, borderWidth, borderHeight);
                DrawFilledRect(new Rect(tabRect.x, tabRect.y, tabRect.width, borderHeight), outer);
                DrawFilledRect(new Rect(tabRect.x, tabRect.yMax - borderHeight, tabRect.width, borderHeight), outer);
                DrawFilledRect(new Rect(tabRect.x, tabRect.y, borderWidth, tabRect.height), outer);
                DrawReferenceLeftTabRightEdge(tabRect, borderWidth);
            }
        }

        private static bool DrawReferenceLeftTabRightEdge(Rect tabRect, float borderWidth)
        {
            Rect rightEdge = new Rect(tabRect.xMax - borderWidth, tabRect.y, borderWidth, tabRect.height);
            DrawFilledRect(rightEdge, Color.black);
            return true;
        }

        private static void DrawReferenceChestTabBackground(Rect tabRect, float borderWidth, float borderHeight)
        {
            float xInset = Mathf.Clamp(borderWidth * 0.75f, 3f, tabRect.width * 0.35f);
            float yInset = Mathf.Clamp(borderHeight * 0.75f, 3f, tabRect.height * 0.35f);
            Rect backgroundRect = new Rect(
                tabRect.x + xInset,
                tabRect.y + yInset,
                Mathf.Max(0f, tabRect.width - xInset * 1.25f),
                Mathf.Max(0f, tabRect.height - yInset * 2f));
            DrawFilledRect(backgroundRect, Color.black);
        }

        private static float GetReferencePageIconScale(PotcoChestPageKind pageKind)
        {
            switch (pageKind)
            {
                case PotcoChestPageKind.WeaponBelt:
                case PotcoChestPageKind.Garb:
                    return 0.22f;
                case PotcoChestPageKind.JewelryAndTattoos:
                case PotcoChestPageKind.PotionsPouch:
                case PotcoChestPageKind.Materials:
                    return 0.12f;
                case PotcoChestPageKind.Ammo:
                    return 0.14f;
                case PotcoChestPageKind.Cards:
                    return 0.4f;
                case PotcoChestPageKind.Treasure:
                    return 0.2f;
                default:
                    return 0.12f;
            }
        }

        private static float GetReferencePageIconLocalExtent(PotcoChestPageKind pageKind)
        {
            switch (pageKind)
            {
                case PotcoChestPageKind.WeaponBelt:
                case PotcoChestPageKind.Garb:
                case PotcoChestPageKind.Cards:
                case PotcoChestPageKind.Treasure:
                    return ReferenceTopLevelIconLocalExtent;
                default:
                    return 1f;
            }
        }

        private void DrawHotbar(GuiReferenceLayout reference)
        {
            Vector2 origin = ReferenceInventoryPagePosition + ReferenceWeaponBagPosition + ReferenceHotbarPosition;
            for (int i = 0; i < layout.HotbarSlots.Count; i++)
            {
                int slot = layout.HotbarSlots[i];
                Vector2 center = origin + new Vector2((i + 0.5f) * ReferenceButtonSize, 0.5f * ReferenceButtonSize);
                Rect cellRect = reference.RectFromCenter(center, ReferenceButtonSize, ReferenceButtonSize);
                DrawSlot(cellRect, slot, layout.HotbarLabels[i]);
            }
        }

        private void DrawGrid(GuiReferenceLayout reference, PotcoChestPageLayout page)
        {
            Vector2 origin = ReferenceInventoryPagePosition + GetContainerReferencePosition(page.Kind);
            int slot = page.FirstSlot;
            for (int row = 0; row < page.Rows; row++)
            {
                for (int column = 0; column < page.Columns; column++)
                {
                    if (slot > page.LastSlot)
                        return;

                    Vector2 center = origin + new Vector2((column + 0.5f) * ReferenceButtonSize, (page.Rows - row - 0.5f) * ReferenceButtonSize);
                    Rect cellRect = reference.RectFromCenter(center, ReferenceButtonSize, ReferenceButtonSize);
                    DrawSlot(cellRect, slot, string.Empty);
                    slot++;
                }
            }
        }

        private void DrawReferenceEquipmentSlots(GuiReferenceLayout reference)
        {
            if (activePage == PotcoChestPageKind.Garb)
            {
                DrawReferenceDressingSlots(reference);
                return;
            }

            if (activePage == PotcoChestPageKind.JewelryAndTattoos)
            {
                DrawReferenceJewelryDressingSlots(reference);
                DrawReferenceTattooDressingSlots(reference);
            }
        }

        private void DrawReferenceDressingSlots(GuiReferenceLayout reference)
        {
            Vector2 origin = ReferenceInventoryPagePosition + ReferenceClothingBagPosition + ReferenceClothingDressingPosition;
            DrawReferenceEquipGrid(reference, origin, 1, 7, ClothingDressingSlots);
        }

        private void DrawReferenceJewelryDressingSlots(GuiReferenceLayout reference)
        {
            Vector2 origin = ReferenceInventoryPagePosition + ReferenceJewelryBagPosition + ReferenceJewelryDressingPosition;
            DrawReferenceEquipGrid(reference, origin, 2, 4, JewelryDressingSlots);
        }

        private void DrawReferenceTattooDressingSlots(GuiReferenceLayout reference)
        {
            Vector2 origin = ReferenceInventoryPagePosition + ReferenceJewelryBagPosition + ReferenceTattooDressingPosition;
            DrawReferenceEquipGrid(reference, origin, 2, 2, TattooDressingSlots);
        }

        private void DrawReferenceEquipGrid(GuiReferenceLayout reference, Vector2 origin, int columns, int rows, IReadOnlyList<ReferenceEquipSlot> slots)
        {
            int index = 0;
            for (int row = rows - 1; row >= 0; row--)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (index >= slots.Count)
                        return;

                    ReferenceEquipSlot slot = slots[index++];
                    Vector2 center = origin + new Vector2((column + 0.5f) * ReferenceButtonSize, (row + 0.5f) * ReferenceButtonSize);
                    Rect cellRect = reference.RectFromCenter(center, ReferenceButtonSize, ReferenceButtonSize);
                    DrawReferenceEquipSlot(reference, cellRect, center, slot);
                }
            }
        }

        private void DrawReferenceEquipSlot(GuiReferenceLayout reference, Rect rect, Vector2 referenceCenter, ReferenceEquipSlot slot)
        {
            LastSlotRects[slot.Location] = rect;
            PotcoInventoryItemStack item = controller.Inventory.GetItemAt(slot.Location);
            bool selected = selectedLocation == slot.Location;
            Event current = Event.current;
            bool hover = current != null && rect.Contains(current.mousePosition);
            Color cellTint = item == null ? ReferenceEquipCellTint : Color.white;

            if (!DrawTextureSprite(rect, selected || hover ? PotcoChestTextureSkin.InventoryBoxOver : PotcoChestTextureSkin.InventoryBox, cellTint))
                DrawFilledRect(rect, item == null ? new Color(0.08f, 0.08f, 0.08f, 0.82f) : new Color(0.10f, 0.08f, 0.06f, 0.45f));

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                HandleSlotClick(slot.Location, item);

            if (item == null)
            {
                DrawReferenceEquipPlaceholder(reference, referenceCenter, slot);
                return;
            }

            Rect iconRect = ExpandFromCenter(rect, 0.9f);
            PotcoGuiSprite itemSprite = resolver.ResolveItemSprite(item.Definition);
            if (!DrawGuiSprite(iconRect, itemSprite, Color.white, false, true) &&
                !DrawRegion(iconRect, resolver.ResolveItemIcon(item.Definition), Color.white, false, true))
                GUI.Label(rect.Contract(4f), ShortName(item.Definition.EffectiveDisplayName), labelStyle);
        }

        private void DrawReferenceEquipPlaceholder(GuiReferenceLayout reference, Vector2 referenceCenter, ReferenceEquipSlot slot)
        {
            if (DrawReferenceGuiSprite(reference, slot.ModelResourcePath, slot.GroupName, referenceCenter, Vector2.one * slot.Scale, ReferenceEquipIconTint, slot.FlipX))
                return;

            Rect iconRect = reference.RectFromCenter(referenceCenter, slot.Scale, slot.Scale);
            DrawRegion(iconRect, resolver.ResolveRegion(slot.ModelResourcePath, slot.GroupName), ReferenceEquipIconTint, slot.FlipX);
        }

        private static Vector2 GetContainerReferencePosition(PotcoChestPageKind pageKind)
        {
            switch (pageKind)
            {
                case PotcoChestPageKind.Garb:
                    return ReferenceClothingBagPosition;
                case PotcoChestPageKind.JewelryAndTattoos:
                    return ReferenceJewelryBagPosition;
                case PotcoChestPageKind.PotionsPouch:
                    return ReferencePotionBagPosition;
                case PotcoChestPageKind.Ammo:
                case PotcoChestPageKind.Materials:
                    return ReferenceAmmoBagPosition;
                case PotcoChestPageKind.Cards:
                    return ReferenceCardsBagPosition;
                case PotcoChestPageKind.WeaponBelt:
                default:
                    return ReferenceWeaponBagPosition;
            }
        }

        private static Vector2 ToReferenceCellCenter(Vector2 containerOrigin)
        {
            return containerOrigin + ReferenceSingleCellOffset;
        }

        private void DrawSlot(Rect rect, int location, string emptyLabel)
        {
            LastSlotRects[location] = rect;
            PotcoInventoryItemStack item = controller.Inventory.GetItemAt(location);
            bool selected = selectedLocation == location;
            Event current = Event.current;
            bool hover = current != null && rect.Contains(current.mousePosition);

            if (!DrawReferenceInventoryCellFrame(rect, selected || hover))
                DrawFilledRect(rect, selected || hover ? new Color(0.72f, 0.58f, 0.34f, 0.65f) : new Color(0.10f, 0.08f, 0.06f, 0.45f));

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                HandleSlotClick(location, item);

            if (item == null)
            {
                if (!string.IsNullOrEmpty(emptyLabel))
                    GUI.Label(new Rect(rect.x + 1f, rect.yMax - 18f, rect.width - 2f, 16f), emptyLabel, labelStyle);
                return;
            }

            Rect iconRect = ExpandFromCenter(rect, 0.9f);
            PotcoGuiSprite itemSprite = resolver.ResolveItemSprite(item.Definition);
            if (!DrawGuiSprite(iconRect, itemSprite, Color.white, false, true) &&
                !DrawRegion(iconRect, resolver.ResolveItemIcon(item.Definition), Color.white, false, true))
                GUI.Label(rect.Contract(4f), ShortName(item.Definition.EffectiveDisplayName), labelStyle);

            if (item.Quantity > 1)
                GUI.Label(new Rect(rect.x + rect.width - 26f, rect.y + rect.height - 18f, 24f, 16f), item.Quantity.ToString(), labelStyle);
        }

        private void DrawTreasure(GuiReferenceLayout reference)
        {
            Rect rect = reference.RectFromBounds(-0.34f, 0.34f, -0.44f, 0.18f);
            GUI.Label(new Rect(rect.x, rect.y + 20f, rect.width, 32f), "Gold", titleStyle);
            GUI.Label(new Rect(rect.x, rect.y + 62f, rect.width, 28f), controller.Inventory.Gold.ToString(), titleStyle);
            DrawReferenceGuiSprite(reference, PotcoRuntimeGuiAssetResolver.TopLevelGui, "treasure_w_coin", Vector2.zero, Vector2.one * 0.22f, Color.white, false, true);
        }

        private void DrawBottomControls(GuiReferenceLayout reference)
        {
            Vector2 page = ReferenceInventoryPagePosition;
            Vector2 trashCenter = page + ToReferenceCellCenter(new Vector2(0.07f, -0.03f));
            Rect trashRect = reference.RectFromCenter(trashCenter, ReferenceButtonSize, ReferenceButtonSize);
            Event current = Event.current;
            bool trashHover = current != null && trashRect.Contains(current.mousePosition);
            DrawReferenceInventoryCellFrame(trashRect, trashHover);
            DrawReferenceBottomControlSprite(reference, PotcoRuntimeGuiAssetResolver.TopLevelGui, trashHover ? "pir_t_gui_but_trash_over" : "pir_t_gui_but_trash", trashCenter, Vector2.one * ReferenceTrashGeomScale, true);
            if (GUI.Button(trashRect, GUIContent.none, GUIStyle.none) && selectedLocation != PotcoInventoryLocations.InvalidLocation)
                TrashSelected();

            if (activePage == PotcoChestPageKind.PotionsPouch)
                DrawReferencePotionDrinker(reference, page + ToReferenceCellCenter(new Vector2(0.30f, -0.03f)));
            else
                DrawReferenceInventoryButton(reference, page + new Vector2(0.37f, 0.04f), "Face Camera");

            DrawReferenceInventoryButton(reference, page + new Vector2(0.68f, 0.04f), "Redeem Code");
            DrawReferenceGoldSlot(reference, page + ToReferenceCellCenter(new Vector2(0.85f, -0.03f)));

            if (showDebugAddControls)
                DrawDebugAddControls(reference);
        }

        private bool DrawReferenceInventoryCellFrame(Rect rect, bool focused)
        {
            return DrawTextureSprite(rect, focused ? PotcoChestTextureSkin.InventoryBoxOver : PotcoChestTextureSkin.InventoryBox, Color.white);
        }

        private void DrawReferencePotionDrinker(GuiReferenceLayout reference, Vector2 referenceCenter)
        {
            Rect cellRect = reference.RectFromCenter(referenceCenter, ReferenceButtonSize, ReferenceButtonSize);
            Event current = Event.current;
            bool hover = current != null && cellRect.Contains(current.mousePosition);
            DrawReferenceInventoryCellFrame(cellRect, hover);

            if (!DrawReferenceGuiSprite(
                    reference,
                    PotcoRuntimeGuiAssetResolver.SkillIconsGui,
                    "pir_t_ico_pot_elixir",
                    referenceCenter,
                    Vector2.one * 0.13f,
                    Color.white))
            {
                DrawRegion(
                    reference.RectFromCenter(referenceCenter, 0.13f, 0.13f),
                    resolver.ResolveRegion(PotcoRuntimeGuiAssetResolver.SkillIconsGui, "pir_t_ico_pot_elixir"),
                    Color.white);
            }

            if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                Debug.Log(controller.Catalog.GetString("HowToDrinkPotion", "Drop a potion here to drink it."));

            string text = controller.Catalog.GetString("DrinkPotion", "Drink\nPotion");
            Rect textRect = reference.RectFromCenter(referenceCenter + new Vector2(0f, -0.025f), ReferenceButtonSize * 0.95f, ReferenceButtonSize * 0.50f);
            GUI.Label(textRect, text, labelStyle);
        }

        private void DrawReferenceGoldSlot(GuiReferenceLayout reference, Vector2 referenceCenter)
        {
            Rect cellRect = reference.RectFromCenter(referenceCenter, ReferenceButtonSize, ReferenceButtonSize);
            Event current = Event.current;
            bool hover = current != null && cellRect.Contains(current.mousePosition);
            if (!DrawReferenceGuiSprite(
                    reference,
                    PotcoRuntimeGuiAssetResolver.SkillIconsGui,
                    hover ? "base_over" : "base",
                    referenceCenter,
                    Vector2.one * ReferenceButtonSize,
                    Color.white,
                    false,
                    true) &&
                !DrawTextureSprite(cellRect, hover ? PotcoChestTextureSkin.SkillBaseOver : PotcoChestTextureSkin.SkillBase, Color.white))
            {
                DrawReferenceInventoryCellFrame(cellRect, hover);
            }

            DrawReferenceGuiSprite(
                reference,
                PotcoRuntimeGuiAssetResolver.TopLevelGui,
                "treasure_w_coin",
                referenceCenter,
                Vector2.one * ReferenceGoldItemImageScale,
                Color.white,
                false,
                true);

            Rect amountRect = reference.RectFromCenter(referenceCenter + new Vector2(0f, -0.058f), ReferenceButtonSize * 0.95f, ReferenceButtonSize * 0.34f);
            GUI.Label(amountRect, controller.Inventory.Gold.ToString(), labelStyle);
        }

        private bool DrawReferenceInventoryButton(GuiReferenceLayout reference, Vector2 referenceCenter, string text)
        {
            Rect hitRect = reference.RectFromCenter(referenceCenter, ReferenceCharGuiButtonWidth, ReferenceCharGuiButtonHeight);
            Event current = Event.current;
            bool hover = current != null && hitRect.Contains(current.mousePosition);
            bool pressed = hover && current != null && current.type == EventType.MouseDown && current.button == 0;
            string spriteName = pressed
                ? PotcoChestTextureSkin.CharGuiTextBlockLargeDown
                : (hover ? PotcoChestTextureSkin.CharGuiTextBlockLargeOver : PotcoChestTextureSkin.CharGuiTextBlockLarge);

            if (!DrawTextureSprite(hitRect, spriteName, Color.white) &&
                !DrawReferenceColorOnlySprite(reference, PotcoRuntimeGuiAssetResolver.CharGui, GetReferenceInventoryButtonGroupName(spriteName), referenceCenter, Vector2.one * ReferenceCharGuiButtonScale))
            {
                DrawFilledRect(hitRect, GUI.enabled ? new Color(0.16f, 0.10f, 0.06f, 0.95f) : new Color(0.10f, 0.09f, 0.08f, 0.95f));
            }

            bool clicked = GUI.Button(hitRect, GUIContent.none, GUIStyle.none);
            Rect textRect = reference.RectFromCenter(referenceCenter + new Vector2(0f, -0.01f), ReferenceCharGuiButtonWidth * 0.96f, 0.06f);
            GUI.Label(textRect, text, buttonTextStyle);
            return clicked;
        }

        private bool DrawReferenceBottomControlSprite(GuiReferenceLayout reference, string modelResourcePath, string groupName, Vector2 referenceCenter, Vector2 referenceScale, bool flipAlphaY)
        {
            return DrawReferenceGuiSprite(reference, modelResourcePath, groupName, referenceCenter, referenceScale, Color.white, false, flipAlphaY);
        }

        private bool DrawReferenceColorOnlySprite(GuiReferenceLayout reference, string modelResourcePath, string groupName, Vector2 referenceCenter, Vector2 referenceScale)
        {
            PotcoGuiSprite sprite = resolver.ResolveSprite(modelResourcePath, groupName);
            if (sprite == null || !sprite.IsDefined)
                return false;

            Rect spriteRect = reference.RectFromLocalBounds(referenceCenter, referenceScale, sprite.LocalBounds);
            return DrawGuiSpriteColorOnly(spriteRect, sprite, Color.white);
        }

        private static string GetReferenceInventoryButtonGroupName(string spriteName)
        {
            switch (spriteName)
            {
                case PotcoChestTextureSkin.CharGuiTextBlockLargeOver:
                    return "chargui_text_block_large_over";
                case PotcoChestTextureSkin.CharGuiTextBlockLargeDown:
                    return "chargui_text_block_large_down";
                case PotcoChestTextureSkin.CharGuiTextBlockLarge:
                default:
                    return "chargui_text_block_large";
            }
        }

        private void DrawDebugAddControls(GuiReferenceLayout reference)
        {
            Rect strip = reference.RectFromBounds(-0.44f, 0.40f, -0.92f, -0.82f);
            DrawFilledRect(strip, new Color(0.03f, 0.02f, 0.015f, 0.82f));

            GUI.Label(new Rect(strip.x + 8f, strip.y + 8f, 34f, 18f), "Item", labelStyle);
            addItemId = GUI.TextField(new Rect(strip.x + 44f, strip.y + 6f, 58f, 22f), addItemId, inputStyle);
            GUI.Label(new Rect(strip.x + 8f, strip.y + 32f, 34f, 18f), "Qty", labelStyle);
            addQuantity = GUI.TextField(new Rect(strip.x + 44f, strip.y + 30f, 58f, 22f), addQuantity, inputStyle);

            if (DrawOriginalButton(new Rect(strip.x + 114f, strip.y + 16f, 74f, 30f), "Add"))
                AddTypedItem();

            bool hasSelection = selectedLocation != PotcoInventoryLocations.InvalidLocation && controller.Inventory.GetItemAt(selectedLocation) != null;
            GUI.enabled = hasSelection;
            if (DrawOriginalButton(new Rect(strip.x + 196f, strip.y + 16f, 72f, 30f), "Equip"))
                EquipSelected();
            if (DrawOriginalButton(new Rect(strip.x + 276f, strip.y + 16f, 62f, 30f), "Use"))
                UseSelected();
            if (DrawOriginalButton(new Rect(strip.x + 346f, strip.y + 16f, 72f, 30f), "Trash"))
                TrashSelected();
            GUI.enabled = true;
        }

        private IReadOnlyList<PotcoChestPageLayout> GetOrderedPages()
        {
            return new[]
            {
                layout.GetPage(PotcoChestPageKind.WeaponBelt),
                layout.GetPage(PotcoChestPageKind.Garb),
                layout.GetPage(PotcoChestPageKind.JewelryAndTattoos),
                layout.GetPage(PotcoChestPageKind.PotionsPouch),
                layout.GetPage(PotcoChestPageKind.Ammo),
                layout.GetPage(PotcoChestPageKind.Materials),
                layout.GetPage(PotcoChestPageKind.Cards),
                layout.GetPage(PotcoChestPageKind.Treasure)
            };
        }

        private void HandleSlotClick(int location, PotcoInventoryItemStack item)
        {
            if (selectedLocation == PotcoInventoryLocations.InvalidLocation)
            {
                if (item != null)
                    selectedLocation = location;
                return;
            }

            if (selectedLocation == location)
            {
                selectedLocation = PotcoInventoryLocations.InvalidLocation;
                return;
            }

            PotcoInventoryMoveResult result = controller.Inventory.MoveItem(selectedLocation, location);
            if (!result.Success)
                Debug.LogWarning(result.Message);
            selectedLocation = PotcoInventoryLocations.InvalidLocation;
        }

        private void AddTypedItem()
        {
            if (!int.TryParse(addItemId, out int itemId))
                return;

            if (!int.TryParse(addQuantity, out int quantity))
                quantity = 1;

            controller.AddItemToInventory(itemId, Mathf.Max(1, quantity));
        }

        private void EquipSelected()
        {
            PotcoInventoryMoveResult result = controller.Inventory.EquipFirstAvailable(selectedLocation);
            if (!result.Success)
                Debug.LogWarning(result.Message);
            selectedLocation = PotcoInventoryLocations.InvalidLocation;
        }

        private void UseSelected()
        {
            PotcoInventoryItemStack selected = controller.Inventory.GetItemAt(selectedLocation);
            if (selected != null && selected.Category == PotcoInventoryCategory.Consumable)
                controller.Inventory.ConsumeOne(selectedLocation);
            selectedLocation = PotcoInventoryLocations.InvalidLocation;
        }

        private void TrashSelected()
        {
            controller.Inventory.TrashItem(selectedLocation);
            selectedLocation = PotcoInventoryLocations.InvalidLocation;
        }

        private void DrawTooltipForMouse(Rect panel)
        {
            Event current = Event.current;
            if (current == null)
                return;

            PotcoInventoryItemStack hovered = FindHoveredItem(current.mousePosition);
            if (hovered == null)
                return;

            Rect tooltip = new Rect(current.mousePosition.x + 18f, current.mousePosition.y + 18f, 250f, 130f);
            if (tooltip.xMax > Screen.width)
                tooltip.x = Screen.width - tooltip.width - 8f;
            if (tooltip.yMax > Screen.height)
                tooltip.y = Screen.height - tooltip.height - 8f;

            PotcoItemDefinition definition = hovered.Definition;
            string rarity = controller.Catalog.GetRarityName(definition.Rarity);
            string body = definition.EffectiveDisplayName;
            if (!string.IsNullOrEmpty(rarity))
                body += "\n" + rarity + " " + definition.Category;
            if (definition.Power > 0)
                body += "\nAttack: " + definition.Power;
            if (definition.StackLimit > 1)
                body += $"\nStack: {hovered.Quantity}/{definition.StackLimit}";
            if (!string.IsNullOrEmpty(definition.FlavorText) && definition.FlavorText != "0")
                body += "\n\n" + definition.FlavorText;

            GUI.Box(tooltip, GUIContent.none);
            GUI.Label(tooltip.Contract(10f), body, tooltipStyle);
        }

        private PotcoInventoryItemStack FindHoveredItem(Vector2 mouse)
        {
            foreach (PotcoInventoryItemStack item in controller.Inventory.ItemsByLocation.Values)
            {
                if (LastSlotRects.TryGetValue(item.Location, out Rect rect) && rect.Contains(mouse))
                    return item;
            }

            return null;
        }

        private readonly Dictionary<int, Rect> LastSlotRects = new Dictionary<int, Rect>();

        private bool DrawReferencePageIcon(PotcoChestPageLayout page, Rect tabRect, bool emphasized, Color tint)
        {
            if (page == null || tabRect.width <= 0f || tabRect.height <= 0f)
                return false;

            float unitScale = tabRect.width / ReferenceSideTabWidth;
            float iconScale = GetReferencePageIconScale(page.Kind) * (emphasized ? 1.1f : 1f);
            string[] models =
            {
                PotcoRuntimeGuiAssetResolver.TopLevelGui,
                PotcoRuntimeGuiAssetResolver.InventoryIconsGui,
                PotcoRuntimeGuiAssetResolver.JewelryIconsGui,
                PotcoRuntimeGuiAssetResolver.ShopIconsGui,
                PotcoRuntimeGuiAssetResolver.ShipMaterialIconsGui,
                PotcoRuntimeGuiAssetResolver.SkillIconsGui,
                PotcoRuntimeGuiAssetResolver.BuffIconsGui
            };

            foreach (string model in models)
            {
                PotcoGuiSprite sprite = resolver.ResolveSprite(model, page.IconGroup);
                if (sprite == null || !sprite.IsDefined)
                    continue;

                Rect iconRect = RectFromScreenLocalBounds(
                    tabRect.center,
                    unitScale,
                    Vector2.one * iconScale,
                    sprite.LocalBounds);
                if (DrawGuiSprite(iconRect, sprite, tint, false, true))
                    return true;
            }

            float fallbackExtent = GetReferencePageIconLocalExtent(page.Kind);
            Rect fallbackBounds = new Rect(-fallbackExtent * 0.5f, -fallbackExtent * 0.5f, fallbackExtent, fallbackExtent);
            Rect fallbackRect = RectFromScreenLocalBounds(
                tabRect.center,
                unitScale,
                Vector2.one * iconScale,
                fallbackBounds);
            PotcoGuiRegion icon = ResolvePageIcon(page);
            return DrawRegion(fallbackRect, icon, tint, false, true);
        }

        private bool DrawGuiSprite(Rect rect, string modelResourcePath, string groupName, Color tint)
        {
            PotcoGuiSprite sprite = resolver.ResolveSprite(modelResourcePath, groupName);
            return DrawGuiSprite(rect, sprite, tint);
        }

        private bool DrawReferenceGuiSprite(GuiReferenceLayout reference, string modelResourcePath, string groupName, Vector2 referencePosition, Vector2 referenceScale, Color tint)
        {
            return DrawReferenceGuiSprite(reference, modelResourcePath, groupName, referencePosition, referenceScale, tint, false);
        }

        private bool DrawReferenceGuiSprite(GuiReferenceLayout reference, string modelResourcePath, string groupName, Vector2 referencePosition, Vector2 referenceScale, Color tint, bool flipX)
        {
            return DrawReferenceGuiSprite(reference, modelResourcePath, groupName, referencePosition, referenceScale, tint, flipX, false);
        }

        private bool DrawReferenceGuiSprite(GuiReferenceLayout reference, string modelResourcePath, string groupName, Vector2 referencePosition, Vector2 referenceScale, Color tint, bool flipX, bool flipAlphaY)
        {
            PotcoGuiSprite sprite = resolver.ResolveSprite(modelResourcePath, groupName);
            if (sprite == null || !sprite.IsDefined)
                return false;

            Rect spriteRect = reference.RectFromLocalBounds(referencePosition, referenceScale, sprite.LocalBounds);
            return DrawGuiSprite(spriteRect, sprite, tint, flipX, flipAlphaY);
        }

        private bool DrawGuiSprite(Rect rect, PotcoGuiSprite sprite, Color tint)
        {
            return DrawGuiSprite(rect, sprite, tint, false);
        }

        private bool DrawGuiSprite(Rect rect, PotcoGuiSprite sprite, Color tint, bool flipX)
        {
            return DrawGuiSprite(rect, sprite, tint, flipX, false);
        }

        private bool DrawGuiSprite(Rect rect, PotcoGuiSprite sprite, Color tint, bool flipX, bool flipAlphaY)
        {
            if (sprite == null || !sprite.IsDefined)
                return false;

            bool drew = false;
            foreach (PotcoGuiSpritePart part in sprite.Parts)
            {
                if (part == null || part.Texture == null)
                    continue;

                if (part.HasTriangleGeometry && DrawGuiSpriteTriangle(rect, sprite.LocalBounds, part, tint, flipX, flipAlphaY))
                {
                    drew = true;
                    continue;
                }

                Rect target = ProjectSpritePart(rect, sprite.LocalBounds, part.LocalRect);
                if (target.width < 0.5f || target.height < 0.5f)
                    continue;

                Rect texCoords = part.TexCoords;
                if (flipX)
                {
                    target.x = rect.xMax - (target.x - rect.x) - target.width;
                    texCoords = FlipTexCoordsX(texCoords);
                }

                DrawTextureWithTexCoords(target, part.Texture, part.AlphaTexture, texCoords, tint, flipAlphaY);
                drew = true;
            }

            return drew;
        }

        private bool DrawGuiSpriteColorOnly(Rect rect, PotcoGuiSprite sprite, Color tint)
        {
            if (sprite == null || !sprite.IsDefined)
                return false;

            bool drew = false;
            foreach (PotcoGuiSpritePart part in sprite.Parts)
            {
                if (part == null || part.Texture == null)
                    continue;

                Rect target = ProjectSpritePart(rect, sprite.LocalBounds, part.LocalRect);
                if (target.width < 0.5f || target.height < 0.5f)
                    continue;

                DrawTextureWithTexCoords(target, part.Texture, null, part.TexCoords, tint);
                drew = true;
            }

            return drew;
        }

        private bool DrawTextureSprite(Rect rect, string spriteName, Color tint)
        {
            if (textureSkin == null || !textureSkin.TryGetSprite(spriteName, out PotcoTextureSprite sprite) || !sprite.IsDefined)
                return false;

            bool drew = false;
            foreach (PotcoTextureSpritePart part in sprite.Parts)
            {
                if (part == null || part.Texture == null)
                    continue;

                Rect target = ProjectSpritePart(rect, sprite.LocalBounds, part.LocalRect);
                if (target.width < 0.5f || target.height < 0.5f)
                    continue;

                DrawTextureWithTexCoords(target, part.Texture, part.AlphaTexture, part.TexCoords, tint);
                drew = true;
            }

            return drew;
        }

        private bool DrawRegion(Rect rect, PotcoGuiRegion region, Color tint)
        {
            return DrawRegion(rect, region, tint, false);
        }

        private bool DrawRegion(Rect rect, PotcoGuiRegion region, Color tint, bool flipX)
        {
            return DrawRegion(rect, region, tint, flipX, false);
        }

        private bool DrawRegion(Rect rect, PotcoGuiRegion region, Color tint, bool flipX, bool flipAlphaY)
        {
            if (region == null || !region.IsDefined || region.Texture == null)
                return false;

            DrawTextureWithTexCoords(rect, region.Texture, region.AlphaTexture, flipX ? FlipTexCoordsX(region.TexCoords) : region.TexCoords, tint, flipAlphaY);
            return true;
        }

        private static Rect FlipTexCoordsX(Rect texCoords)
        {
            return new Rect(texCoords.xMax, texCoords.y, -texCoords.width, texCoords.height);
        }

        private void DrawTextureWithTexCoords(Rect rect, Texture2D texture, Texture2D alphaTexture, Rect texCoords, Color tint, bool flipAlphaY = false)
        {
            Material alphaMaterial = alphaTexture != null ? GetGuiAlphaMaterial(alphaTexture, flipAlphaY) : null;
            if (alphaMaterial != null)
            {
                Graphics.DrawTexture(rect, texture, texCoords, 0, 0, 0, 0, tint, alphaMaterial);
                return;
            }

            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, texture, texCoords, true);
            GUI.color = old;
        }

        private Material GetGuiAlphaMaterial(Texture2D alphaTexture, bool flipAlphaY)
        {
            if (alphaTexture == null)
                return null;

            string key = alphaTexture.GetInstanceID().ToString() + (flipAlphaY ? "|flip" : "|normal");
            if (guiAlphaMaterials.TryGetValue(key, out Material cached) && cached != null)
                return cached;

            Shader shader = Resources.Load<Shader>(GuiAlphaShaderResource);
            if (shader == null)
                shader = Shader.Find("POTCO/GuiTextureWithAlpha");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetTexture("_AlphaTex", alphaTexture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_FlipAlphaY", flipAlphaY ? 1f : 0f);
            guiAlphaMaterials[key] = material;
            return material;
        }

        private bool DrawOriginalButton(Rect rect, string text)
        {
            Event current = Event.current;
            bool hover = current != null && rect.Contains(current.mousePosition);
            string group = !GUI.enabled
                ? PotcoChestTextureSkin.GenericButtonDisabled
                : (hover ? PotcoChestTextureSkin.GenericButtonOver : PotcoChestTextureSkin.GenericButton);

            if (!DrawTextureSprite(rect, group, Color.white))
                DrawFilledRect(rect, GUI.enabled ? new Color(0.16f, 0.10f, 0.06f, 0.95f) : new Color(0.10f, 0.09f, 0.08f, 0.95f));

            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.Label(rect, text, buttonTextStyle);
            return clicked;
        }

        private static string GetTrayChestSpriteName(bool isOpen, bool hover)
        {
            if (isOpen)
                return hover ? PotcoChestTextureSkin.TreasureChestOpenOver : PotcoChestTextureSkin.TreasureChestOpen;

            return hover ? PotcoChestTextureSkin.TreasureChestClosedOver : PotcoChestTextureSkin.TreasureChestClosed;
        }

        private static string GetTrayChestTextureName(bool isOpen, bool hover)
        {
            if (isOpen)
                return hover ? "treasure_chest_open_over" : "treasure_chest_open";

            return hover ? "treasure_chest_closed_over" : "treasure_chest_closed";
        }

        private bool DrawGuiSpriteTriangle(Rect destination, Rect localBounds, PotcoGuiSpritePart part, Color tint, bool flipX, bool flipAlphaY)
        {
            if (part == null || !part.HasTriangleGeometry || part.Texture == null || localBounds.width <= 0f || localBounds.height <= 0f)
                return false;

            Event current = Event.current;
            if (current != null && current.type != EventType.Repaint)
                return true;

            Texture2D alphaTexture = part.AlphaTexture != null ? part.AlphaTexture : Texture2D.whiteTexture;
            Material material = GetGuiAlphaMaterial(alphaTexture, flipAlphaY);
            if (material == null)
                return false;

            material.SetTexture("_MainTex", part.Texture);
            material.SetTexture("_AlphaTex", alphaTexture);
            material.SetFloat("_FlipAlphaY", flipAlphaY ? 1f : 0f);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            material.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(tint);

            for (int i = 0; i < part.LocalVertices.Count; i++)
            {
                Vector2 screen = ProjectSpritePoint(destination, localBounds, part.LocalVertices[i]);
                if (flipX)
                    screen.x = destination.xMax - (screen.x - destination.x);

                Vector2 uv = part.UvVertices[i];
                GL.TexCoord2(uv.x, uv.y);
                GL.Vertex3(screen.x, screen.y, 0f);
            }

            GL.End();
            GL.PopMatrix();
            return true;
        }

        private static Rect RectFromButtonLocalBounds(Rect buttonRect, Vector2 localPosition, Vector2 localScale, Rect localBounds)
        {
            float unitScaleX = buttonRect.width / ReferenceChestTrayButtonSize;
            float unitScaleY = buttonRect.height / ReferenceChestTrayButtonSize;
            float minX = buttonRect.x + (localPosition.x + localBounds.xMin * localScale.x) * unitScaleX;
            float maxX = buttonRect.x + (localPosition.x + localBounds.xMax * localScale.x) * unitScaleX;
            float minZ = localPosition.y + localBounds.yMin * localScale.y;
            float maxZ = localPosition.y + localBounds.yMax * localScale.y;
            return Rect.MinMaxRect(
                minX,
                buttonRect.yMax - maxZ * unitScaleY,
                maxX,
                buttonRect.yMax - minZ * unitScaleY);
        }

        private static Rect RectFromScreenLocalBounds(Vector2 screenPosition, float unitScale, Vector2 localScale, Rect localBounds)
        {
            float minX = screenPosition.x + localBounds.xMin * localScale.x * unitScale;
            float maxX = screenPosition.x + localBounds.xMax * localScale.x * unitScale;
            float minZ = localBounds.yMin * localScale.y * unitScale;
            float maxZ = localBounds.yMax * localScale.y * unitScale;
            return Rect.MinMaxRect(
                minX,
                screenPosition.y - maxZ,
                maxX,
                screenPosition.y - minZ);
        }

        private static Vector2 ProjectSpritePoint(Rect destination, Rect localBounds, Vector2 localPoint)
        {
            return new Vector2(
                destination.x + ((localPoint.x - localBounds.xMin) / localBounds.width) * destination.width,
                destination.y + ((localBounds.yMax - localPoint.y) / localBounds.height) * destination.height);
        }

        private static Rect ProjectSpritePart(Rect destination, Rect localBounds, Rect localRect)
        {
            float x = destination.x + ((localRect.xMin - localBounds.xMin) / localBounds.width) * destination.width;
            float y = destination.y + ((localBounds.yMax - localRect.yMax) / localBounds.height) * destination.height;
            float width = (localRect.width / localBounds.width) * destination.width;
            float height = (localRect.height / localBounds.height) * destination.height;
            return new Rect(x, y, width, height);
        }

        private static Rect ExpandFromCenter(Rect rect, float scale)
        {
            if (scale <= 0f)
                return rect;

            float width = rect.width * scale;
            float height = rect.height * scale;
            return new Rect(rect.center.x - width * 0.5f, rect.center.y - height * 0.5f, width, height);
        }

        private static void DrawFilledRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private PotcoGuiRegion ResolvePageIcon(PotcoChestPageLayout page)
        {
            string[] models =
            {
                PotcoRuntimeGuiAssetResolver.TopLevelGui,
                PotcoRuntimeGuiAssetResolver.InventoryIconsGui,
                PotcoRuntimeGuiAssetResolver.JewelryIconsGui,
                PotcoRuntimeGuiAssetResolver.ShopIconsGui,
                PotcoRuntimeGuiAssetResolver.ShipMaterialIconsGui,
                PotcoRuntimeGuiAssetResolver.SkillIconsGui,
                PotcoRuntimeGuiAssetResolver.BuffIconsGui
            };

            foreach (string model in models)
            {
                PotcoGuiRegion region = resolver.ResolveRegion(model, page.IconGroup);
                if (region.IsDefined)
                    return region;
            }

            return resolver.ResolveLooseTexture(page.IconGroup);
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "?";

            string[] parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "?";

            return parts[0].Length <= 7 ? parts[0] : parts[0].Substring(0, 7);
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

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.96f, 0.90f, 0.78f) }
            };

            tabStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            activeTabStyle = new GUIStyle(tabStyle)
            {
                normal = { textColor = new Color(1f, 0.86f, 0.36f) }
            };

            slotStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            selectedSlotStyle = new GUIStyle(slotStyle)
            {
                normal = { textColor = new Color(1f, 0.86f, 0.36f) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            tooltipStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = 12,
                normal = { textColor = new Color(0.96f, 0.90f, 0.78f) }
            };

            buttonTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.96f, 0.90f, 0.78f) }
            };

            inputStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.96f, 0.90f, 0.78f), background = GUI.skin.textField.normal.background },
                focused = { textColor = Color.white, background = GUI.skin.textField.focused.background }
            };
        }

        private readonly struct ReferenceTrayButton
        {
            public ReferenceTrayButton(string command, string modelResourcePath, string groupName, float geomScale, float localExtent, string hotkeyLabel)
            {
                Command = command ?? string.Empty;
                ModelResourcePath = modelResourcePath ?? string.Empty;
                GroupName = groupName ?? string.Empty;
                GeomScale = geomScale;
                LocalExtent = localExtent;
                HotkeyLabel = hotkeyLabel ?? string.Empty;
            }

            public string Command { get; }
            public string ModelResourcePath { get; }
            public string GroupName { get; }
            public float GeomScale { get; }
            public float LocalExtent { get; }
            public string HotkeyLabel { get; }
        }

        private readonly struct ReferenceEquipSlot
        {
            public ReferenceEquipSlot(int location, string modelResourcePath, string groupName, float scale, bool flipX)
            {
                Location = location;
                ModelResourcePath = modelResourcePath ?? string.Empty;
                GroupName = groupName ?? string.Empty;
                Scale = scale;
                FlipX = flipX;
            }

            public int Location { get; }
            public string ModelResourcePath { get; }
            public string GroupName { get; }
            public float Scale { get; }
            public bool FlipX { get; }
        }

        private readonly struct GuiReferenceLayout
        {
            private GuiReferenceLayout(Vector2 originScreen, float unitScale)
            {
                OriginScreen = originScreen;
                UnitScale = unitScale;
            }

            public Vector2 OriginScreen { get; }
            public float UnitScale { get; }

            public static GuiReferenceLayout FromPanel(Rect panel)
            {
                float referenceWidth = ReferenceFrameMaxX - ReferenceFrameMinX;
                float referenceHeight = ReferenceFrameMaxZ - ReferenceFrameMinZ;
                float unitScale = Mathf.Min(panel.width / referenceWidth, panel.height / referenceHeight);
                Vector2 origin = new Vector2(
                    panel.x - ReferenceFrameMinX * unitScale,
                    panel.y + ReferenceFrameMaxZ * unitScale);
                return new GuiReferenceLayout(origin, unitScale);
            }

            public Rect RectFromCenter(Vector2 referenceCenter, float referenceWidth, float referenceHeight)
            {
                Vector2 center = Point(referenceCenter);
                float width = referenceWidth * UnitScale;
                float height = referenceHeight * UnitScale;
                return new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
            }

            public Rect RectFromBounds(float minX, float maxX, float minZ, float maxZ)
            {
                Vector2 topLeft = Point(new Vector2(minX, maxZ));
                Vector2 bottomRight = Point(new Vector2(maxX, minZ));
                return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
            }

            public Rect RectFromLocalBounds(Vector2 referencePosition, Vector2 referenceScale, Rect localBounds)
            {
                float minX = referencePosition.x + localBounds.xMin * referenceScale.x;
                float maxX = referencePosition.x + localBounds.xMax * referenceScale.x;
                float minZ = referencePosition.y + localBounds.yMin * referenceScale.y;
                float maxZ = referencePosition.y + localBounds.yMax * referenceScale.y;
                return RectFromBounds(minX, maxX, minZ, maxZ);
            }

            private Vector2 Point(Vector2 referencePoint)
            {
                return new Vector2(
                    OriginScreen.x + referencePoint.x * UnitScale,
                    OriginScreen.y - referencePoint.y * UnitScale);
            }
        }
    }

    internal static class PotcoGuiRectExtensions
    {
        public static Rect Contract(this Rect rect, float amount)
        {
            return new Rect(rect.x + amount, rect.y + amount, Mathf.Max(0f, rect.width - amount * 2f), Mathf.Max(0f, rect.height - amount * 2f));
        }
    }
}
