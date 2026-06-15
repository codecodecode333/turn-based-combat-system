# Turn-Based Combat System Portfolio

Unity 기반 턴제 전술 전투 시스템 개인 프로젝트입니다.

Grid 기반 전투, AP(Action Point) 시스템, 상태이상, Utility AI 등을 중심으로 전투 구조와 게임플레이 흐름 설계에 집중했습니다.

<img width="871" height="488" alt="image" src="https://github.com/user-attachments/assets/56d2db36-271e-4682-b988-4b0fcd4fd5d9" />


---

# Tech Stack

- Unity
- C#
- Git

---

# Core Systems

## Grid System
- Tile 기반 Grid 전투 환경 구현
- BFS 기반 Pathfinding 처리
- 이동 가능 범위 탐색 및 타일 하이라이트 지원

## Turn-Based Combat System
- AP(Action Point) 기반 턴 시스템 구현
- 이동 / 공격 / 스킬 사용 흐름 처리
- 연속 행동 및 턴 종료 로직 구현
- 행동 취소(Cancel) 흐름 지원

## Skill System
- Skill / SkillEffect 기반 구조 설계
- 단일 대상 및 범위 스킬 처리
- 스킬 타겟 선택 및 검증 로직 구현

## Status Effect System
- StatusEffect 기반 상태이상 구조 설계
- Burn / Poison / Stun 상태이상 구현
- 턴 시작 / 종료 시점 이벤트 처리

## AI System
- Utility 기반 AI 의사결정 시스템 구현
- 행동 우선순위 계산
- 이동 / 공격 / 스킬 선택 로직 처리

## UI / UX
- Unit HUD 구현
- 행동 가능 범위 시각화
- 타겟 선택 및 피드백 처리

## VFX Integration
- 공격 및 상태이상 이펙트 연동
- 전투 피드백 강화

---

# Technical Focus

## Object-Oriented Architecture

전투 시스템의 확장성을 위해 SkillEffect / StatusEffect 기반 구조를 사용했습니다.

각 기능을 독립적으로 분리하여:
- 신규 스킬 추가
- 상태이상 확장
- 효과 조합
- AI 행동 추가

등이 가능하도록 설계했습니다.

---

## Gameplay Flow Management

BattleController 중심으로:
- 턴 진행
- 입력 처리
- 행동 실행
- 상태 갱신

흐름을 관리했습니다.

행동 취소 시:
- AP 복원
- 위치 복원
- 상태 동기화

등을 처리하여 전투 상태 불일치 문제를 해결했습니다.

---

# Troubleshooting

## Action Cancel State Mismatch

행동 취소 시:
- AP
- 위치
- 행동 상태

가 불일치하는 문제가 발생했습니다.

행동 이전 상태를 저장하고 복원하는 구조를 추가하여 문제를 해결했습니다.

---

## Continuous Action Flow

AP가 남아있는 경우 행동이 강제로 종료되는 문제가 있었습니다.

행동 종료 조건을 수정하여:
- 이동 후 스킬 사용
- 스킬 연속 사용

등의 흐름을 지원하도록 개선했습니다.

---

# Project Structure

- BattleController
- BattleInput
- CombatResolver
- CombatTargetResolver
- AIPlanner
- GridManager
- TileHighlighter
- UnitHud

중심으로 전투 로직을 구성했습니다.

---

# Gameplay Video

- (영상 링크 추가 예정)

---

# Development

Personal Project
