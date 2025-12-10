import pandas as pd
import numpy as np
import glob
import os
import random
import json

# ===========================================================
# 1. 設定・パラメータ
# ===========================================================

LOG_DIR = 'Assets/DemoLogs'
EXPORT_DIR = 'Assets/DemoAIs'
EXPORT_FILENAME_Q = 'ai-model_q_table'
EXPORT_FILENAME_IL = 'ai-model_il_policy'

GAMMA = 0.97
ALPHA = 0.1
EPOCHS = 50
ACTION_SPACE = 4

# データ拡張回数
AUG_COUNT = 5


# -----------------------------------------------------------
# 2. 状態エンコード関数
# -----------------------------------------------------------
def encode_state(x, y, env, up, down, right, left):
    # C#の BuildStateKey と完全に一致させる
    # C#側: $"({x}, {y}, {env}, {up}, {down}, {right}, {left})"
    return f"({x}, {y}, {env}, {up}, {down}, {right}, {left})"


# ===========================================================
# 3. CSVログ読み込み
# ===========================================================

def load_logs(log_dir):
    print(f"'{log_dir}' からログファイルを読み込み中...")

    all_experiences = []
    il_data = []

    log_files = sorted(glob.glob(os.path.join(log_dir, 'Plog*.csv')))
    if not log_files:
        print("ログがありません。")
        return None, None

    for file in log_files:
        try:
            df = pd.read_csv(file).dropna()

            required = ['nowX','nowY','env','envUp','envDown','envRight','envLeft','action','reward']
            if any(col not in df.columns for col in required):
                print(f"{file} に必要な列がありません")
                continue

            df['nowX'] = df['nowX'].round().astype(int)
            df['nowY'] = df['nowY'].round().astype(int)

            # データ長を取得
            max_idx = len(df) - 1

            for i in range(len(df)):
                row = df.iloc[i]

                # 状態 S
                s_key = encode_state(
                    row['nowX'], row['nowY'],
                    row['env'], row['envUp'], row['envDown'],
                    row['envRight'], row['envLeft']
                )

                action = int(row['action'])
                reward = float(row['reward'])

                # ILデータ（本物）
                il_data.append((s_key, action))

                # 終端判定
                is_goal = (reward >= 1000)
                is_fall = (reward <= -500)
                is_last_step = (i == max_idx)

                # ゴールか落下、もしくはログの物理的な最後であれば終端とする
                terminal = (is_goal or is_fall or is_last_step)

                # 次状態 S'
                if not terminal:
                    next_row = df.iloc[i+1]
                    s_next_key = encode_state(
                        next_row['nowX'], next_row['nowY'],
                        next_row['env'], next_row['envUp'], next_row['envDown'],
                        next_row['envRight'], next_row['envLeft']
                    )
                else:
                    s_next_key = None

                all_experiences.append((s_key, action, reward, s_next_key, terminal))

        except Exception as e:
            print(f"ファイル {file} の読み込み中にエラー: {e}")

    print(f"読み込み完了: Q学習データ {len(all_experiences)} 件, ILデータ {len(il_data)} 件")
    return all_experiences, il_data


# ===========================================================
# 4. 模倣学習（IL）
# ===========================================================

def imitation_learning(il_data):
    print("模倣学習を開始...")

    if not il_data:
        print("ILデータがありません")
        return {}

    count_dict = {}

    for state_key, action in il_data:
        if state_key not in count_dict:
            count_dict[state_key] = []
        count_dict[state_key].append(action)

    il_policy = {
        s: max(set(actions), key=actions.count)
        for s, actions in count_dict.items()
    }

    print(f"模倣学習完了（{len(il_policy)} 状態）")
    return il_policy


# ===========================================================
# 5. Q-learning本体 + ILボーナス
# ===========================================================

def q_learning_training(experiences, il_policy):
    print("Q学習を開始...")
    if not experiences:
        return None

    q_table = {}

    def get_q_row(state_key):
        if state_key not in q_table:
            q_table[state_key] = [0, 0, 0, 0, 0]
        return q_table[state_key]

    for epoch in range(EPOCHS):
        random.shuffle(experiences)

        for s_key, action, reward, s_prime_key, terminal in experiences:

            q_row = get_q_row(s_key)

            # 次状態のmaxQ
            if terminal or s_prime_key is None:
                max_q_prime = 0
            else:
                q_row_prime = get_q_row(s_prime_key)
                max_q_prime = max(q_row_prime[1:5])

            # IL一致ボーナス
            il_bonus = 0.5 if il_policy.get(s_key) == action else 0.0

            # Q更新
            target = reward + il_bonus + GAMMA * max_q_prime
            q_row[action] = (1 - ALPHA) * q_row[action] + ALPHA * target

        if (epoch+1) % 5 == 0:
            print(f"  {epoch+1}/{EPOCHS} epochs 完了")

    print("Q学習が完了しました")
    return q_table


# ===========================================================
# 6. モデル保存
# ===========================================================

def export_models(q_table, il_policy):
    print(f"'{EXPORT_DIR}' にモデルを保存中...")
    os.makedirs(EXPORT_DIR, exist_ok=True)

    if q_table:
        q_path = os.path.join(EXPORT_DIR, EXPORT_FILENAME_Q + ".json")
        dict_to_save = {str(k): v for k, v in q_table.items()}
        with open(q_path, 'w') as f:
            json.dump(dict_to_save, f)
        print("Qテーブル保存完了")

    if il_policy:
        il_path = os.path.join(EXPORT_DIR, EXPORT_FILENAME_IL + ".json")
        dict_to_save = {str(k): v for k, v in il_policy.items()}
        with open(il_path, 'w') as f:
            json.dump(dict_to_save, f, indent=4)
        print("ILポリシー保存完了")


# ===========================================================
# 7. メイン処理
# ===========================================================

if __name__ == '__main__':
    experiences, il_data = load_logs(LOG_DIR)

    if experiences:
        il_policy = imitation_learning(il_data)
        q_table = q_learning_training(experiences, il_policy)
        export_models(q_table, il_policy)

        print("\n--- AI学習完了 ---")
    else:
        print("ログがないため終了します。")
