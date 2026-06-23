# Turn-Based Combat System Portfolio

Unity 기반 아이소메트릭 턴제 전술 전투 시스템 개인 프로젝트입니다.

Grid 기반 이동, Height System, LOS(Line of Sight), AP(Action Point), Turn Queue, Action Queue, 상태이상, Utility AI를 중심으로 전투 시스템 구조와 게임플레이 흐름 설계에 집중했습니다.

---

## Screenshots

<img width="935" height="524" alt="image" src="https://github.com/user-attachments/assets/5f38f2f8-8f69-4cc3-9df2-cfa45c7aa2bd" />

<img width="932" height="521" alt="image" src="https://github.com/user-attachments/assets/eff0ae9c-4248-4c9d-9d47-dcc4222f51d9" />

<img width="933" height="523" alt="image" src="https://github.com/user-attachments/assets/2f8712e4-fc89-432e-99ad-e876467a7349" />

<img width="934" height="524" alt="image" src="https://github.com/user-attachments/assets/a1565723-9023-4b16-a811-e766c81faad7" />

---

## Tech Stack

- Unity
- C#
- Git

---

## Core Systems

### Grid & Movement System

- Tile 기반 Grid 전투 환경 구현
- BFS 기반 이동 가능 범위 탐색
- 이동 경로 Preview 및 Highlight 처리
- Height Level 기반 이동 가능 여부 판정

### Turn-Based Combat System

- Speed 기반 Turn Queue 구현
- AP(Action Point) 기반 행동 시스템 구현
- 이동, 공격, 스킬 사용, 턴 종료 흐름 처리
- Action Queue 기반 행동 예약 및 실행 구조 설계
- 행동 취소(Cancel) 흐름 지원

### Skill System

- SkillData 기반 스킬 데이터 관리
- 단일 대상 및 범위 스킬 처리
- 스킬 사거리, 최소 사거리, AP 비용 검증
- LOS(Line of Sight) 기반 타겟 판정

### Status Effect System

- Burn, Poison, Stun 상태이상 구현
- 턴 시작 / 종료 시점 Tick 처리
- 확장 가능한 StatusEffect 구조 설계

### AI System

- Utility 기반 AI 의사결정 시스템 구현
- 행동 후보 탐색 및 점수 계산
- 이동 / 공격 / 스킬 사용 우선순위 결정
- 피해량, 처치 가능 여부, 타겟 수 기반 평가

### UI / UX

- Unit HUD 구현
- HP / AP UI 구현
- Skill Tooltip 구현
- Unit Info Panel 구현
- 이동 범위 및 타겟 범위 시각화

### VFX Integration

- Projectile 및 Hit FX 연동
- Burn, Stun 상태이상 FX 연동
- 선택 타일 Highlight FX 구현

---

## Technical Focus

### Object-Oriented Combat Architecture

전투 시스템의 확장성을 위해 Skill, StatusEffect, Combat 처리 로직을 분리하여 설계했습니다.

이를 통해 다음 기능을 독립적으로 확장할 수 있도록 구성했습니다.

- 신규 스킬 추가
- 상태이상 추가
- 스킬 효과 조합
- AI 행동 추가
- UI 및 VFX 연동

---

### Gameplay Flow Management

BattleController를 중심으로 전투 흐름을 관리했습니다.

```text
Turn Start
    ↓
Unit Select
    ↓
Move / Skill Select
    ↓
Action Queue
    ↓
Confirm
    ↓
Action Execute
    ↓
Status Update
    ↓
Turn End
```

Action Queue를 사용하여 플레이어가 이동과 스킬을 선택한 뒤 Confirm 입력 시 실행되도록 구성했습니다.

---

### Tactical Rule Validation

전술 게임에 필요한 다양한 제약 조건을 검증했습니다.

- 이동 가능 거리
- Height 차이
- Skill Range
- Minimum Range
- Line of Sight
- AP Cost
- Target Type
- Tile Occupancy

이를 통해 전략적인 의사결정이 가능하도록 설계했습니다.

---

## Troubleshooting

### 1. Action Cancel State Mismatch

#### Problem

행동 취소 시 다음 상태가 서로 불일치하는 문제가 발생했습니다.

- Unit Position
- AP
- Selected Action
- Tile Highlight
- Preview State

#### Solution

행동 실행 전 상태를 저장하고, 취소 시 해당 상태를 복원하는 구조를 추가했습니다.

이를 통해 이동 취소, 스킬 선택 취소, ESC 입력 시 전투 상태가 일관되게 유지되도록 개선했습니다.

---

### 2. Continuous Action Flow

#### Problem

AP가 남아 있어도 행동이 강제로 종료되는 문제가 있었습니다.

이로 인해 AP 기반 전투 시스템의 핵심인 연속 행동이 불가능했습니다.

#### Solution

행동 종료 조건을 수정하여 AP가 남아있는 경우 추가 행동을 선택할 수 있도록 개선했습니다.

지원되는 흐름

- 이동 후 스킬 사용
- 스킬 연속 사용
- 행동 취소 후 재선택
- AP 부족 시 행동 제한

---

### 3. Tile Sorting Issue

#### Problem

아이소메트릭 Tilemap에서 서로 다른 타일을 혼합하여 사용할 경우 동일한 Height Level에서도 정렬 순서가 어긋나는 문제가 발생했습니다.

#### Solution

Sprite Atlas를 적용하여 타일 리소스 관리 방식을 통일하고 정렬 문제를 개선했습니다.

---

### 4. UI Fill Rendering Issue

#### Problem

HP Bar 및 AP UI 구현 과정에서 Fill 이미지가 비정상적으로 줄어들거나 Edge가 깨지는 문제가 발생했습니다.

#### Solution

1x1 Pixel Sprite 기반 Fill 이미지를 사용하여 Scaling 및 Fill 문제를 해결했습니다.

---

## Project Structure

```text
BattleController
BattleInput
CombatResolver
CombatTargetResolver
AIPlanner
GridManager
TileHighlighter
UnitHud
SkillData
StatusEffect
```

### BattleController

- 전체 전투 흐름 관리
- 턴 시작 / 종료 처리
- 플레이어 입력 흐름 제어
- Action Queue 실행 관리

### GridManager

- Grid 좌표 및 Tile 정보 관리
- 이동 가능 여부 판정
- Height 및 TileData 제공

### AIPlanner

- 적 유닛 행동 후보 탐색
- 스킬 및 타겟 평가
- 최종 행동 선택

### CombatResolver

- 공격 및 스킬 효과 처리
- Damage / Heal / Status Effect 적용

### TileHighlighter

- 이동 범위, 공격 범위, 타겟 범위 시각화
- Hover 및 Preview 상태 표시

---

## Future Improvements

- 플레이 영상 추가
- ScriptableObject 기반 스킬 데이터 구조 보강
- AI 행동 평가 가중치 개선
- 전투 로그 시스템 추가
- 사운드 및 카메라 Shake 추가
- Roguelike 성장 시스템 확장

---

## Gameplay Video

- 영상 링크 추가 예정

---

## Development

Personal Project

## Author

Kim Minjae
