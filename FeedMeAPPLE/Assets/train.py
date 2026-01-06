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
EXPORT_BC_POLICY = 'bc_policy.json'              # 行動クローニング（確率分布）
EXPORT_REWARD_GRADIENT = 'reward_gradient.json'  # 報酬勾配テーブル
EXPORT_GOAL_POSITIONS = 'goal_positions.json'    # 推定ゴール座標

# 報酬閾値
GOAL_REWARD_THRESHOLD = 1000
FALL_REWARD_THRESHOLD = -500


# ===========================================================
# 2. 状態エンコード関数
# ===========================================================

def encode_state_with_coords(x, y, env, up, down, right, left):
    """座標付き状態キー（ゴール推定用）"""
    return f"({x}, {y}, {env}, {up}, {down}, {right}, {left})"

def encode_state_env_only(env, up, down, right, left):
    """環境パターンのみの状態キー（行動決定用）"""
    return f"({env}, {up}, {down}, {right}, {left})"


# ===========================================================
# 3. CSVログ読み込み
# ===========================================================

def load_logs(log_dir):
    """
    ログファイルを読み込み、以下のデータを返す:
    - bc_data: 状態→行動のリスト（行動クローニング用）
    - reward_data: 状態→報酬のリスト（報酬勾配用）
    - goal_coords: ゴール座標リスト（ゴール推定用）
    """
    print(f"'{log_dir}' からログファイルを読み込み中...")

    bc_data = defaultdict(list)       # state_key -> [action, action, ...]
    reward_data = defaultdict(list)   # state_key -> [reward, reward, ...]
    goal_coords = []                  # [(x, y), ...]

    log_files = sorted(glob.glob(os.path.join(log_dir, 'Plog*.csv')))
    if not log_files:
        print("ログがありません。")
        return None, None, None

    total_steps = 0

    for file in log_files:
        try:
            df = pd.read_csv(file).dropna()

            required = ['nowX', 'nowY', 'env', 'envUp', 'envDown', 'envRight', 'envLeft', 'action', 'reward']
            if any(col not in df.columns for col in required):
                print(f"{file} に必要な列がありません。スキップします。")
                continue

            # 座標を整数化
            df['nowX'] = df['nowX'].round().astype(int)
            df['nowY'] = df['nowY'].round().astype(int)

            for i in range(len(df)):
                row = df.iloc[i]

                x = int(row['nowX'])
                y = int(row['nowY'])
                env = int(row['env'])
                up = int(row['envUp'])
                down = int(row['envDown'])
                right = int(row['envRight'])
                left = int(row['envLeft'])
                action = int(row['action'])
                reward = float(row['reward'])

                # 環境パターンのみの状態キー（座標非依存）
                state_key = encode_state_env_only(env, up, down, right, left)

                # 行動クローニングデータ
                bc_data[state_key].append(action)

                # 報酬勾配データ
                reward_data[state_key].append(reward)

                # ゴール座標の記録（報酬が高い場所）
                if reward >= GOAL_REWARD_THRESHOLD:
                    goal_coords.append((x, y))

                total_steps += 1

        except Exception as e:
            print(f"ファイル {file} の読み込み中にエラー: {e}")

    print(f"読み込み完了: {len(log_files)} ファイル, {total_steps} ステップ")
    print(f"  - BC状態数: {len(bc_data)}")
    print(f"  - 報酬勾配状態数: {len(reward_data)}")
    print(f"  - ゴール到達回数: {len(goal_coords)}")

    return bc_data, reward_data, goal_coords


# ===========================================================
# 4. 行動クローニング（BC Policy）生成
# ===========================================================

def build_bc_policy(bc_data):
    """
    各状態での行動出現回数を確率分布として保存
    出力形式: {state_key: {"1": count, "2": count, "3": count, "4": count}}
    """
    print("\n行動クローニング（BC Policy）を生成中...")

    if not bc_data:
        print("BCデータがありません")
        return None

    bc_policy = {}

    for state_key, actions in bc_data.items():
        # 各行動の出現回数をカウント
        action_counts = {"1": 0, "2": 0, "3": 0, "4": 0}
        for action in actions:
            if 1 <= action <= 4:
                action_counts[str(action)] += 1

        bc_policy[state_key] = action_counts

    print(f"BC Policy生成完了: {len(bc_policy)} 状態")

    # 統計情報を表示
    total_actions = sum(sum(counts.values()) for counts in bc_policy.values())
    print(f"  - 総行動サンプル数: {total_actions}")

    return bc_policy


# ===========================================================
# 5. 報酬勾配テーブル生成
# ===========================================================

def build_reward_gradient(reward_data):
    """
    各状態での報酬統計（平均、最大、最小、サンプル数）を生成
    出力形式: {state_key: {"avg": float, "max": float, "min": float, "count": int}}
    """
    print("\n報酬勾配テーブルを生成中...")

    if not reward_data:
        print("報酬データがありません")
        return None

    reward_gradient = {}

    for state_key, rewards in reward_data.items():
        if len(rewards) == 0:
            continue

        reward_gradient[state_key] = {
            "avg": float(np.mean(rewards)),
            "max": float(np.max(rewards)),
            "min": float(np.min(rewards)),
            "count": len(rewards)
        }

    print(f"報酬勾配テーブル生成完了: {len(reward_gradient)} 状態")

    # 統計情報を表示
    all_avgs = [stats["avg"] for stats in reward_gradient.values()]
    print(f"  - 平均報酬の範囲: {min(all_avgs):.2f} ~ {max(all_avgs):.2f}")

    return reward_gradient


# ===========================================================
# 6. ゴール座標推定
# ===========================================================

def build_goal_positions(goal_coords):
    """
    報酬1000以上が出現した座標の頻度を記録
    出力形式: {"(x, y)": count, ...}
    """
    print("\nゴール座標を推定中...")

    if not goal_coords:
        print("ゴール到達データがありません")
        return None

    goal_positions = defaultdict(int)

    for x, y in goal_coords:
        key = f"({x}, {y})"
        goal_positions[key] += 1

    # 辞書に変換
    goal_positions = dict(goal_positions)

    print(f"ゴール座標推定完了: {len(goal_positions)} 箇所")

    # 上位5箇所を表示
    sorted_goals = sorted(goal_positions.items(), key=lambda x: x[1], reverse=True)
    print("  - 頻出ゴール座標:")
    for coord, count in sorted_goals[:5]:
        print(f"    {coord}: {count}回")

    return goal_positions


# ===========================================================
# 7. モデル保存
# ===========================================================

def export_models(bc_policy, reward_gradient, goal_positions):
    """3つのJSONファイルを出力"""
    print(f"\n'{EXPORT_DIR}' にモデルを保存中...")
    os.makedirs(EXPORT_DIR, exist_ok=True)

    # BC Policy保存
    if bc_policy:
        bc_path = os.path.join(EXPORT_DIR, EXPORT_BC_POLICY)
        with open(bc_path, 'w', encoding='utf-8') as f:
            json.dump(bc_policy, f, indent=2, ensure_ascii=False)
        print(f"BC Policy保存完了: {bc_path}")

    # 報酬勾配テーブル保存
    if reward_gradient:
        rg_path = os.path.join(EXPORT_DIR, EXPORT_REWARD_GRADIENT)
        with open(rg_path, 'w', encoding='utf-8') as f:
            json.dump(reward_gradient, f, indent=2, ensure_ascii=False)
        print(f"報酬勾配テーブル保存完了: {rg_path}")

    # ゴール座標保存
    if goal_positions:
        gp_path = os.path.join(EXPORT_DIR, EXPORT_GOAL_POSITIONS)
        with open(gp_path, 'w', encoding='utf-8') as f:
            json.dump(goal_positions, f, indent=2, ensure_ascii=False)
        print(f"ゴール座標保存完了: {gp_path}")


# ===========================================================
# 8. メイン処理
# ===========================================================

if __name__ == '__main__':
    print("=" * 60)
    print("プレイヤー行動傾向学習システム")
    print("=" * 60)

    # ログ読み込み
    bc_data, reward_data, goal_coords = load_logs(LOG_DIR)

    if bc_data:
        # 各モデルを生成
        bc_policy = build_bc_policy(bc_data)
        reward_gradient = build_reward_gradient(reward_data)
        goal_positions = build_goal_positions(goal_coords)

        # モデル保存
        export_models(bc_policy, reward_gradient, goal_positions)

        print("\n" + "=" * 60)
        print("学習完了！")
        print("=" * 60)
    else:
        print("\nログがないため終了します。")
