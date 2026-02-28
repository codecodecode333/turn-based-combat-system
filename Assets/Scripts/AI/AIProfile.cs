using UnityEngine;

[CreateAssetMenu(menuName = "Battle/AI/Profile", fileName = "AIProfile_")]
public class AIProfile : ScriptableObject
{
    [Header("Candidate Limits")]
    [Min(1)] public int maxTilesToEvaluate = 30;   // moveRange BFS 타일이 많아질 때 컷오프
    [Min(1)] public int topK = 2;                  // 상위 K개 중 선택(난이도/자연스러움)

    [Header("Randomness")]
    [Range(0f, 1f)] public float mistakeChance = 0.15f; // Easy는 높게, Hard는 낮게

    [Header("Weights (Utility)")]
    public float weightDamage = 1.0f;   // 딜 가치
    public float weightKill = 8.0f;     // 처치 보너스
    public float weightBurn = 0.6f;     // Burn 기대값(직딜 대비 가중)
    public float weightThreat = 1.0f;   // 위험(위협도) 페널티

    [Header("Targeting")]
    public float weightFocusLowHP = 1.0f; // HP 낮은 적 선호
    public float weightNearest = 0.1f;    // 가까운 적 선호(너무 크면 맨날 달라붙음)

    [Header("Approach")]
    public float weightApproach = 0.8f; // 공격 불가 시 접근 유도 (값 클수록 더 잘 붙음)

    [Header("Ranged Spacing")]
    public float weightKeepRange = 2.1f;   // 사거리 밴드 밖이면 페널티 (클수록 거리 유지)
    public int keepRangeBuffer = 1;        // 너무 끝사거리/너무 근접 피하려는 완충
}