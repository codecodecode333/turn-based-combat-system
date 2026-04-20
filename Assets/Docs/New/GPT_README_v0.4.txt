# GPT_README.txt v0.4

## 목적

본 문서는 이 프로젝트에서 ChatGPT를 활용한 개발 방식,
프로젝트 구조, 설계 원칙, 협업 규칙을 정의한다.

이 문서 하나로 다음이 가능해야 한다.

* 프로젝트 현재 상태 파악
* 코드 구조 이해
* 설계 철학 이해
* 다음 작업 방향 결정
* GPT와 동일한 맥락 유지

---

# 1. 프로젝트 개요

## 1.1 장르

* 턴제 전술 전투
* 로그라이크 요소 포함 예정
* 퍼즐 + 전투 혼합 구조

## 1.2 핵심 목표

```plaintext id="core_goal"
속도감
파괴감
전략성
직관적인 UX
```

---

# 2. 현재 개발 단계

```plaintext id="dev_stage"
9차 목표: 전투 폴리싱
```

현재 상태:

* 전투 시스템 핵심 완성
* UX / Highlight / Preview 거의 정리됨
* 상태이상 로직 완성 (FX 미완)
* AI 1차 완성
* 타일 / Height / Hazard 구조 확립

---

# 3. 전투 핵심 구조

```plaintext id="combat_core"
Move (0~1회, AP 없음)
Skill (AP 기반 N회)
```

* Move → Skill만 가능
* Skill 이후 Move 불가
* AP가 허용하는 만큼 스킬 사용 가능

---

# 4. 핵심 시스템 요약

## 4.1 Preview 시스템

```plaintext id="preview_rule"
Preview = Execution
```

* 모든 계산은 previewPosition 기준
* Preview / 실제 실행 / AI가 동일 규칙 사용

---

## 4.2 Highlight 시스템

```plaintext id="highlight_rule"
Base Overlay + FX Overlay
```

* Base: Move / Range / Preview
* FX: Selected / Target / Warning / Hazard

---

## 4.3 상태이상

```plaintext id="status_rule"
상태이상 = 월드 FX 중심
```

* HUD 아이콘 최소화
* 상태는 독립 인스턴스 (큐 방식)
* Tick / 반응 / 지속 구조

---

## 4.4 스킬 시스템

* AP 기반 다중 사용
* TargetMode 기반 타겟팅
* SkillEffect 체인 구조
* Friendly Fire = 스킬별

---

## 4.5 AI

* 후보 생성 → 점수 계산 → 선택
* 성향 기반 행동
* 완전 최적이 아닌 “인간형 AI”

---

## 4.6 타일 시스템

* Height = 이동/강제이동에만 적용
* LOS = blocksLOS 기반
* Hazard = Overlay 개념
* Base Terrain + Hazard 분리

---

# 5. 코드 구조 핵심

핵심 6축:

```plaintext id="core_classes"
BattleController
GridManager
CombatTargetResolver
CombatResolver
Unit
AIPlanner
```

## 레이어 구조

```plaintext id="layer_structure"
Presentation
→ Battle Flow
→ Rules / Resolution
→ World / Grid
→ Unit / State
→ Data
→ AI
```

---

# 6. GPT 협업 방식

## 6.1 기본 원칙

* GPT는 설계 + 구조 + 패치 방향을 제시
* 실제 코드 적용은 사용자 주도
* GPT는 항상 현재 구조를 기준으로 답변

---

## 6.2 작업 방식

```plaintext id="workflow"
1. 질문 → 설계 결정
2. 체크리스트 생성
3. 패치 코드 생성
4. 테스트
5. 커밋
```

---

## 6.3 문서 작성 방식

모든 문서는 다음 흐름으로 작성한다.

```plaintext id="doc_flow"
질문 → 결정 → v2 문서 작성
```

---

## 6.4 문서 관리 규칙

```plaintext id="doc_rule"
/docs/v2      = 최신 문서
/docs/archive = 구버전
```

* 기존 문서 수정 ❌
* v2 새 문서 생성 ⭕
* archive 유지

---

# 7. 개발 우선순위

현재 기준:

```plaintext id="priority"
1. 상태이상 월드 이펙트
2. Highlight FX 완성
3. PreviewActor 정리
```

---

# 8. 향후 로드맵

## 10차 목표: 전투 완성

* UX 완성
* 상태이상 FX 완성
* 버그 제거

## 11차 목표: 로그라이크 시스템

* 스킬 선택
* 강화 시스템
* 보상 구조

## 12차 목표: 밸런싱

* 전투 수치 조정
* 상태 밸런스

## 13차 목표: 메타게임

* 성장 시스템
* 장비
* 진행 구조

---

# 9. 설계 철학

## 9.1 단순하지만 깊게

* 규칙은 단순하게
* 조합으로 깊이를 만든다

## 9.2 예측 가능성

```plaintext id="predictability"
플레이어는 결과를 예측할 수 있어야 한다
```

* 랜덤 요소 최소화
* Preview와 실제 결과 일치

## 9.3 속도감

* 불필요한 입력 제거
* 빠른 턴 진행
* 직관적인 피드백

## 9.4 파괴감

* 강한 스킬
* 연쇄 효과
* 상태이상 시각화

---

# 10. GPT에게 요청할 때 규칙

## 10.1 좋은 요청

* “현재 스냅샷 기준 분석해줘”
* “붙여넣기 패치 코드”
* “체크리스트로 정리”
* “v2 문서로 만들어줘”

## 10.2 피해야 할 요청

* 모호한 요구
* 현재 구조 무시한 질문
* 지나치게 광범위한 요구

---

# 11. 앞으로 GPT 활용 전략

* 설계 결정: GPT 활용
* 구조 정리: GPT 활용
* 디버깅: GPT + 실제 테스트 병행
* 밸런싱: GPT 보조 + 플레이 테스트 중심

---

# 12. 핵심 요약

```plaintext id="summary"
이 프로젝트는
Preview 일관성, 단순한 규칙, 빠른 UX, 강한 전투 피드백
을 중심으로 설계된 턴제 전술 게임이다.

GPT는 설계 파트너이며,
문서는 프로젝트의 단일 진실(source of truth)이다.
```

---

END
