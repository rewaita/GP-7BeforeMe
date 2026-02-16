import pandas as pd
import numpy as np
import glob
import os
import json
from collections import defaultdict

# ===========================================================
# 1. 設定・パラメータ
# ===========================================================

LOG_DIR = 'Assets/DemoLogs'
EXPORT_DIR = 'Assets/DemoAIs'

# 出力ファイル名
EXPORT_MODEL_DATA = 'model_data.json'     # メインモデルデータ
EXPORT_PARAMETERS = 'parameters.json'     # 人間可読パラメータ

# 報酬閾値
GOAL_REWARD_THRESHOLD = 500   # ゴール判定（報酬500以上）
FALL_REWARD_THRESHOLD = -100  # 穴落下判定（大きなマイナス報酬）

# マス情報の定義
TILE_HOLE = 0   # 穴
TILE_FLAT = 1   # 平地
TILE_GOAL = 2   # ゴール
TILE_TRAP = 3   # 罠

# 行動の定義
ACTION_UP = 1
ACTION_RIGHT = 2
ACTION_DOWN = 3
ACTION_LEFT = 4


# ===========================================================
# 2. 状態エンコード関数
# ===========================================================

def encode_surroundings(up, down, right, left):
    """周囲4マスの状態をキーとしてエンコード"""
    return f"{up},{down},{right},{left}"


# ===========================================================
# 3. CSVログ読み込み
# ===========================================================

def load_logs(log_dir):
    """
    7個のCSVログファイルを読み込み、以下のデータを返す:
    - all_records: 全ステップのレコードリスト
    - visited_coords: 訪問した座標の集合
    - danger_coords: 危険地帯の座標リスト
    - goal_coords: ゴール座標リスト
    """
    print(f"'{log_dir}' からログファイルを読み込み中...")

    all_records = []           # 全レコード
    visited_coords = set()     # 訪問済み座標
    danger_coords = []         # 危険地帯（穴落下箇所）
    goal_coords = []           # ゴール座標

    log_files = sorted(glob.glob(os.path.join(log_dir, 'Plog*.csv')))
    if not log_files:
        print("ログがありません。")
        return None, None, None, None

    print(f"  読み込み対象: {len(log_files)} ファイル")

    for file in log_files:
        try:
            df = pd.read_csv(file).dropna()

            # CSVヘッダー: times,nowX,nowY,env,envUp,envDown,envRight,envLeft,action,reward
            required = ['times', 'nowX', 'nowY', 'env', 'envUp', 'envDown', 
                       'envRight', 'envLeft', 'action', 'reward']
            if any(col not in df.columns for col in required):
                print(f"  {os.path.basename(file)}: 必要な列がありません。スキップ。")
                continue

            # 座標を整数化
            df['nowX'] = df['nowX'].round().astype(int)
            df['nowY'] = df['nowY'].round().astype(int)

            prev_record = None
            for i in range(len(df)):
                row = df.iloc[i]

                record = {
                    'times': int(row['times']),
                    'x': int(row['nowX']),
                    'y': int(row['nowY']),
                    'env': int(row['env']),
                    'up': int(row['envUp']),
                    'down': int(row['envDown']),
                    'right': int(row['envRight']),
                    'left': int(row['envLeft']),
                    'action': int(row['action']),
                    'reward': float(row['reward'])
                }

                all_records.append(record)
                visited_coords.add((record['x'], record['y']))

                # ゴール到達の検出
                if record['reward'] >= GOAL_REWARD_THRESHOLD or record['env'] == TILE_GOAL:
                    goal_coords.append((record['x'], record['y']))

                # 危険地帯の検出（穴落下 = 大きなマイナス報酬）
                if record['reward'] <= FALL_REWARD_THRESHOLD or record['env'] == TILE_HOLE:
                    danger_coords.append((record['x'], record['y']))

                prev_record = record

            print(f"  {os.path.basename(file)}: {len(df)} ステップ")

        except Exception as e:
            print(f"  {os.path.basename(file)}: エラー - {e}")

    print(f"\n読み込み完了:")
    print(f"  - 総ステップ数: {len(all_records)}")
    print(f"  - 訪問座標数: {len(visited_coords)}")
    print(f"  - ゴール到達回数: {len(goal_coords)}")
    print(f"  - 危険地帯検出: {len(danger_coords)}")

    return all_records, visited_coords, danger_coords, goal_coords


# ===========================================================
# 4. 知識抽出フェーズ
# ===========================================================

def extract_knowledge(all_records, visited_coords, danger_coords, goal_coords):
    """
    知識抽出：ゴール推定、危険地帯、未踏領域を特定
    """
    print("\n" + "=" * 60)
    print("知識抽出フェーズ")
    print("=" * 60)

    knowledge = {
        'estimated_goal': None,
        'danger_zones': [],
        'unexplored': []
    }

    # ゴールの推定（最頻出座標）
    if goal_coords:
        goal_freq = defaultdict(int)
        for coord in goal_coords:
            goal_freq[coord] += 1
        most_common_goal = max(goal_freq.items(), key=lambda x: x[1])
        knowledge['estimated_goal'] = most_common_goal[0]
        print(f"  ゴール推定: ({most_common_goal[0][0]}, {most_common_goal[0][1]}) - {most_common_goal[1]}回到達")
    else:
        print("  ゴール: 不明（一度も到達していない）")

    # 危険地帯の特定（穴の座標）
    if danger_coords:
        danger_freq = defaultdict(int)
        for coord in danger_coords:
            danger_freq[coord] += 1
        # 頻度順にソート
        sorted_dangers = sorted(danger_freq.items(), key=lambda x: x[1], reverse=True)
        knowledge['danger_zones'] = [coord for coord, _ in sorted_dangers[:10]]  # 上位10箇所
        print(f"  危険地帯: {len(danger_freq)} 箇所検出")
        for coord, count in sorted_dangers[:5]:
            print(f"    ({coord[0]}, {coord[1]}): {count}回落下")

    # 未踏領域の推定（訪問済み座標の境界から推測）
    if visited_coords:
        min_x = min(c[0] for c in visited_coords)
        max_x = max(c[0] for c in visited_coords)
        min_y = min(c[1] for c in visited_coords)
        max_y = max(c[1] for c in visited_coords)
        
        # 探索範囲内で訪問していない座標
        for x in range(min_x, max_x + 1):
            for y in range(min_y, max_y + 1):
                if (x, y) not in visited_coords:
                    knowledge['unexplored'].append((x, y))
        
        print(f"  未踏領域: {len(knowledge['unexplored'])} 箇所（探索範囲内）")

    return knowledge


# ===========================================================
# 5. 行動クローニング（BC）生成
# ===========================================================

def build_behavior_cloning(all_records):
    """
    周囲4マスの状態をキーとし、行動確率分布をテーブル化
    """
    print("\n" + "=" * 60)
    print("行動クローニング（BC）生成")
    print("=" * 60)

    if not all_records:
        print("  レコードがありません")
        return {}

    # 周囲状態 → 行動カウント
    bc_table = defaultdict(lambda: {1: 0, 2: 0, 3: 0, 4: 0})

    for record in all_records:
        state_key = encode_surroundings(
            record['up'], record['down'], 
            record['right'], record['left']
        )
        action = record['action']
        if 1 <= action <= 4:
            bc_table[state_key][action] += 1

    # 確率分布に変換
    bc_policy = {}
    for state_key, action_counts in bc_table.items():
        total = sum(action_counts.values())
        if total > 0:
            bc_policy[state_key] = {
                "up": round(action_counts[1] / total, 4),
                "right": round(action_counts[2] / total, 4),
                "down": round(action_counts[3] / total, 4),
                "left": round(action_counts[4] / total, 4),
                "samples": total
            }

    print(f"  BC状態数: {len(bc_policy)}")
    print(f"  総サンプル数: {sum(p['samples'] for p in bc_policy.values())}")

    return bc_policy


# ===========================================================
# 6. 決定木重み（スコア重み）生成
# ===========================================================

def build_decision_weights(all_records, knowledge):
    """
    穴/罠の回避重み、ゴール方向バイアスなどを算出
    """
    print("\n" + "=" * 60)
    print("決定木重み（スコア重み）生成")
    print("=" * 60)

    if not all_records:
        print("  レコードがありません")
        return {}

    # 各タイル種別が隣接時の行動パターンを分析
    hole_avoidance = {'approach': 0, 'avoid': 0}  # 穴への接近/回避
    trap_behavior = {'approach': 0, 'avoid': 0}   # 罠への接近/回避
    goal_behavior = {'approach': 0, 'avoid': 0}   # ゴールへの接近/回避

    # ゴール方向への移動傾向
    goal_direction_match = 0
    goal_direction_total = 0

    estimated_goal = knowledge.get('estimated_goal')

    for record in all_records:
        action = record['action']
        x, y = record['x'], record['y']
        
        # 周囲のタイル情報
        neighbors = {
            ACTION_UP: record['up'],
            ACTION_RIGHT: record['right'],
            ACTION_DOWN: record['down'],
            ACTION_LEFT: record['left']
        }

        # 行動先のタイル種別
        target_tile = neighbors.get(action, TILE_FLAT)

        # 穴の分析
        hole_neighbors = [d for d, t in neighbors.items() if t == TILE_HOLE]
        if hole_neighbors:
            if action in hole_neighbors or target_tile == TILE_HOLE:
                hole_avoidance['approach'] += 1
            else:
                hole_avoidance['avoid'] += 1

        # 罠の分析
        trap_neighbors = [d for d, t in neighbors.items() if t == TILE_TRAP]
        if trap_neighbors:
            if action in trap_neighbors or target_tile == TILE_TRAP:
                trap_behavior['approach'] += 1
            else:
                trap_behavior['avoid'] += 1

        # ゴールの分析
        goal_neighbors = [d for d, t in neighbors.items() if t == TILE_GOAL]
        if goal_neighbors:
            if action in goal_neighbors or target_tile == TILE_GOAL:
                goal_behavior['approach'] += 1
            else:
                goal_behavior['avoid'] += 1

        # ゴール方向への移動傾向（ゴール座標が既知の場合）
        if estimated_goal:
            gx, gy = estimated_goal
            dx = gx - x
            dy = gy - y

            # 行動がゴール方向かどうか
            goal_direction = []
            if dy > 0:
                goal_direction.append(ACTION_UP)
            if dy < 0:
                goal_direction.append(ACTION_DOWN)
            if dx > 0:
                goal_direction.append(ACTION_RIGHT)
            if dx < 0:
                goal_direction.append(ACTION_LEFT)

            if goal_direction:
                goal_direction_total += 1
                if action in goal_direction:
                    goal_direction_match += 1

    # 重みの計算
    weights = {}

    # 穴回避指数（-5.0 ~ 0.0、負の値ほど回避傾向）
    hole_total = hole_avoidance['approach'] + hole_avoidance['avoid']
    if hole_total > 0:
        hole_ratio = hole_avoidance['approach'] / hole_total
        weights['hole_fear_index'] = round(-5.0 * (1.0 - hole_ratio), 2)
    else:
        weights['hole_fear_index'] = -2.5  # デフォルト

    # 罠への積極性（-1.0 ~ 1.0）
    trap_total = trap_behavior['approach'] + trap_behavior['avoid']
    if trap_total > 0:
        trap_ratio = trap_behavior['approach'] / trap_total
        weights['trap_interest'] = round(2.0 * trap_ratio - 1.0, 2)
    else:
        weights['trap_interest'] = 0.0  # デフォルト

    # ゴール優先度（0.0 ~ 1.0）
    if goal_direction_total > 0:
        weights['goal_bias'] = round(goal_direction_match / goal_direction_total, 2)
    else:
        weights['goal_bias'] = 0.5  # デフォルト

    # ゴール接近傾向
    goal_total = goal_behavior['approach'] + goal_behavior['avoid']
    if goal_total > 0:
        weights['goal_approach_rate'] = round(goal_behavior['approach'] / goal_total, 2)
    else:
        weights['goal_approach_rate'] = 0.5

    print(f"  穴回避指数: {weights['hole_fear_index']}")
    print(f"  罠への積極性: {weights['trap_interest']}")
    print(f"  ゴール優先度: {weights['goal_bias']}")
    print(f"  ゴール接近率: {weights['goal_approach_rate']}")

    return weights


# ===========================================================
# 7. 方向別スコアテーブル生成
# ===========================================================

def build_direction_scores(all_records, knowledge):
    """
    ゴール方向・タイル種別ごとのスコア重みを生成
    C#でif文や加算で処理できる形式
    """
    print("\n" + "=" * 60)
    print("方向別スコアテーブル生成")
    print("=" * 60)

    scores = {
        # タイル種別ごとのスコア（行動先がこのタイルの場合）
        "tile_scores": {
            "hole": -100,    # 穴: 絶対回避
            "flat": 0,       # 平地: 中立
            "goal": 100,     # ゴール: 最優先
            "trap": -10      # 罠: やや回避
        },
        # ゴール方向ボーナス
        "goal_direction_bonus": 10,
        # 既訪問ペナルティ
        "revisit_penalty": -5,
        # 未探索ボーナス
        "unexplored_bonus": 5
    }

    # 実データから調整
    if all_records:
        # 報酬の統計から調整
        rewards = [r['reward'] for r in all_records]
        avg_reward = np.mean(rewards)
        
        # 穴に落ちた時の平均報酬から穴スコアを調整
        hole_rewards = [r['reward'] for r in all_records if r['env'] == TILE_HOLE]
        if hole_rewards:
            scores['tile_scores']['hole'] = int(np.mean(hole_rewards))

        # ゴール到達時の報酬から調整
        goal_rewards = [r['reward'] for r in all_records if r['reward'] >= GOAL_REWARD_THRESHOLD]
        if goal_rewards:
            scores['tile_scores']['goal'] = int(np.mean(goal_rewards) / 10)  # スケール調整

    print(f"  タイルスコア: {scores['tile_scores']}")
    print(f"  ゴール方向ボーナス: {scores['goal_direction_bonus']}")

    return scores


# ===========================================================
# 8. モデル保存
# ===========================================================

def export_models(knowledge, bc_policy, weights, direction_scores):
    """model_data.json と parameters.json を出力"""
    print("\n" + "=" * 60)
    print(f"'{EXPORT_DIR}' にモデルを保存中...")
    print("=" * 60)
    
    os.makedirs(EXPORT_DIR, exist_ok=True)

    # 1. model_data.json（Unity読み込み用）
    model_data = {
        "version": "1.0",
        "generated_at": pd.Timestamp.now().isoformat(),
        
        # 推定されたゴール座標
        "estimated_goal": {
            "x": knowledge['estimated_goal'][0] if knowledge['estimated_goal'] else -1,
            "y": knowledge['estimated_goal'][1] if knowledge['estimated_goal'] else -1,
            "known": knowledge['estimated_goal'] is not None
        },
        
        # 危険地帯リスト
        "danger_zones": [
            {"x": coord[0], "y": coord[1]} 
            for coord in knowledge.get('danger_zones', [])
        ],
        
        # 行動クローニングテーブル（周囲状況 → 行動確率）
        "bc_policy": bc_policy,
        
        # 決定木用スコア重み
        "decision_weights": weights,
        
        # 方向別スコアテーブル
        "direction_scores": direction_scores
    }

    model_path = os.path.join(EXPORT_DIR, EXPORT_MODEL_DATA)
    with open(model_path, 'w', encoding='utf-8') as f:
        json.dump(model_data, f, indent=2, ensure_ascii=False)
    print(f"  model_data.json 保存完了: {model_path}")

    # 2. parameters.json（人間可読用）
    goal_str = f"{knowledge['estimated_goal'][0]},{knowledge['estimated_goal'][1]}" if knowledge['estimated_goal'] else "unknown"
    
    parameters = {
        "estimated_goal": goal_str,
        "hole_fear_index": weights.get('hole_fear_index', -2.5),
        "trap_interest": weights.get('trap_interest', 0.0),
        "goal_bias": weights.get('goal_bias', 0.5),
        "description": {
            "estimated_goal": "推定されたゴール座標 (x,y)",
            "hole_fear_index": "穴を避ける強さ（負の値ほど回避傾向）",
            "trap_interest": "罠への積極性（-1.0=回避、1.0=積極）",
            "goal_bias": "ゴールを優先する度合い（0.0~1.0）"
        },
        "statistics": {
            "bc_states": len(bc_policy),
            "danger_zones": len(knowledge.get('danger_zones', [])),
            "unexplored_areas": len(knowledge.get('unexplored', []))
        }
    }

    param_path = os.path.join(EXPORT_DIR, EXPORT_PARAMETERS)
    with open(param_path, 'w', encoding='utf-8') as f:
        json.dump(parameters, f, indent=2, ensure_ascii=False)
    print(f"  parameters.json 保存完了: {param_path}")


# ===========================================================
# 9. メイン処理
# ===========================================================

if __name__ == '__main__':
    print("=" * 60)
    print("プレイヤー行動傾向学習システム v2.0")
    print("ハイブリッド方式（決定木 + 行動クローニング）")
    print("=" * 60)

    # ログ読み込み
    all_records, visited_coords, danger_coords, goal_coords = load_logs(LOG_DIR)

    if all_records:
        # 知識抽出
        knowledge = extract_knowledge(all_records, visited_coords, danger_coords, goal_coords)

        # 行動クローニング生成
        bc_policy = build_behavior_cloning(all_records)

        # 決定木重み生成
        weights = build_decision_weights(all_records, knowledge)

        # 方向別スコアテーブル生成
        direction_scores = build_direction_scores(all_records, knowledge)

        # モデル保存
        export_models(knowledge, bc_policy, weights, direction_scores)

        print("\n" + "=" * 60)
        print("学習完了！")
        print("=" * 60)
        print("\n出力ファイル:")
        print(f"  1. {EXPORT_DIR}/{EXPORT_MODEL_DATA}")
        print(f"  2. {EXPORT_DIR}/{EXPORT_PARAMETERS}")
        print("\nUnity C#でJSONをパースし、if文と加算処理で推論できます。")
    else:
        print("\nログがないため終了します。")
