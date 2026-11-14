# Red Frontier resource HUD

Copy the RedFrontierOutpost folder into Assets in the receiving Unity project. Keep the .meta files. Created with Unity 6.6 and uGUI.

Open OutpostHudPreview.unity and press Play. For a pixel-for-pixel preview, use a 1156 × 691 Game view. The supplied screenshot is the background. The new resource bar covers the old bar at the top. The terrain and water upgrade tree are part of the original screenshot.

Drag OutpostHud.prefab into the actual gameplay scene as a root object and disable the old resource strip there. The prefab only displays resources and lets clicks pass through. The screenshot and preview scene are optional and can be omitted from the shipping scene.

Call OutpostHud.SetResource(resource, amount, capacity, netPerSecond) from the existing resource system. The resource identifiers are Energy, Water, Food, Oxygen, and Metal. Omit netPerSecond when there is no rate to display. Use the existing game's values and rates. The preview shows 30 / 100 Energy and +0.5/s Metal from the screenshot.
