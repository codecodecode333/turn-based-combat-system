using UnityEngine;
public enum SkillTiming
{
    Immediate,     // 버튼 누르자마자
    OnAttackHit,   // 공격 애니메이션의 OnAttackHit
    OnAttackEnd    // 공격 애니메이션 종료 시
}

public enum SkillTargetType
{
    Self,           // 자기 자신
    SingleEnemy,    // 적 1명
    SingleAlly,     // 아군 1명 (자기 제외 가능)
    AllEnemies,     // 적 전체
    AllAllies       // 아군 전체
}


[CreateAssetMenu(menuName = "Battle/Skill")]
public class SkillData : ScriptableObject
{
    public string skillName;

    [Header("Animation")]
    public string animationTrigger;   // triggerAttack / triggerCast / triggerAttack2 등
    public SkillTiming timing;

    [Header("Effect")]
    public SkillEffect[] effects;

    [Header("Target")]
    public SkillTargetType targetType;

    [Header("Range (Manhattan)")]
    public int minRange = 0;
    public int maxRange = 1;
}
