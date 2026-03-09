using UnityEngine;

public enum SkillTiming
{
    Immediate,
    OnAttackHit,
    OnAttackEnd
}

// ✅ 신규: 타겟 모드
public enum SkillTargetMode
{
    AutoNearestSingle,     // 클릭 없이, 사거리 내 가장 가까운 1개
    ClickSingle,           // 유닛 클릭 1개
    ClickTileAOE,          // 타일 클릭 중심 AOE
    AllEnemiesInRange,     // 사거리 내 적 전체
    AllEnemiesAnywhere,    // 전체 적
    AllAlliesInRange,      // 사거리 내 아군 전체
    AllAlliesAnywhere      // 전체 아군
}

[CreateAssetMenu(menuName = "Battle/Skill")]
public class SkillData : ScriptableObject
{
    public string skillName;

    [Header("Animation")]
    public string animationTrigger;
    public SkillTiming timing;

    [Header("Effect")]
    public SkillEffect[] effects;

    [Header("Target (NEW)")]
    public SkillTargetMode targetMode = SkillTargetMode.ClickSingle;

    [Tooltip("ClickTileAOE에서 사용. 중심 타일로부터 Manhattan 반경")]
    public int aoeRadius = 0;

    [Header("Range (Manhattan)")]
    public int minRange = 0;
    public int maxRange = 1;

    [Header("LOS")]
    public bool requiresLineOfSight = false;

    [Header("Cost")]
    [Min(0)] public int costAP = 1;
}