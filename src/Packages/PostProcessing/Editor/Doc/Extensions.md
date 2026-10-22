# 预设与体积权重过渡

`PostProcessPresets.Warm()` 与 `Noir()` 创建独立的可编辑 Profile：暖色调与低强度泛光，或黑白、高对比度与暗角。每次调用返回独立实例。

`PostProcessVolumeTransition` 通过 FadeTo(weight, duration) 平滑改变 PostProcessVolume.weight，使用平滑起止曲线，可在中途改目标或 Cancel。useUnscaledTime 可选择不受 timeScale 影响的过渡；零时长立即完成，completed 每次完成只触发一次。过渡修改体积权重，不修改共享 Profile。

示例工程：Main → Post-process Preset Transitions。Warm、Noir、Fade Out 演示两秒过渡及中途切换。
