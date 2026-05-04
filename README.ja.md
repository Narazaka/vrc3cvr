# VRC3CVR

VRChat SDK3のアバターをChilloutVR用に変換します。

- 元のプロジェクトのバックアップを必ず取ってください。
- 本プロジェクトは、元のプロジェクト（ https://github.com/imagitama/vrc3cvr ）をフォークしたプロジェクト（ https://github.com/SaracenOne/vrc3cvr ）をさらにフォークしたものです。
- Locomotionレイヤーの変換は非推奨です。FXとGestureレイヤーのみの変換を推奨します。

## 動作確認済み環境

- VRChat Avatar SDK3 3.10.2
- ChilloutVR CCK CCK_4.0.0
- Unity 2022.3.22f1（VRChat SDK互換のバージョン）

## 変換で行われること

全体的にPhysBone固有の機能とFX/Gestureレイヤー以外が必要な機能を除けば多くの部分がそのまま動作します。

- ChilloutVR用アバターコンポーネントの追加（存在しない場合）
- 顔メッシュの設定
- Viseme（口パク）の設定
- まばたきブレンドシェイプの設定
- 視点位置・ボイス位置の設定（VRChatアバターの設定を元に）
- 各パラメータに対応する高度な設定UIの追加
  - Float：スライダー
  - Boolean：トグル
  - Int：ドロップダウン（1つだけならトグル）
- 各Animator Controller（Gesture、FXなど）をCVR用に変換  
  - `GestureLeftWeight` / `GestureRightWeight` を `GestureLeft` / `GestureRight` に変換（Fistアニメーションを確認してください！）
  - VRCParameterDriverなども変換
- VRC Contact SenderとReceiverをCVR PointerとCVR Advanced Avatar Triggerに変換
  - VRCContactと違って、CVR PointerやTriggerはContactが衝突した時にしか値を変更しません。この差異によって互換性の問題を生じる可能性があります。
  - プレイ中のContactのShape Typeの変更は非対応です。

### 非対応シェーダー

ChilloutVRはVRChatでも以前叫ばれたSPS-I (Single Pass Stereo Instancing)を実装しているプラットフォームです（VRChatでは結局行われなかった）。

このためSPS-I未対応なシェーダーは描画がバグる（たとえばVRで片目にしか表示されない）ことがあります。

lilToon等は問題ないですが、マイナーなシェーダーは問題になるかもしれません。

## 使い方

### ツール

- VRC3CVR: [Releases](https://github.com/Narazaka/vrc3cvr/releases/latest)にアクセスし、「Assets」を展開して`.unitypackage`をダウンロードします。
- CCK4: [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/)
- Prefabulous for Platform Conversions（VRC Contraintsを普通のConstraintsに変換する）: [Prefabulous](https://docs.hai-vr.dev/docs/products/prefabulous#download) からリスティングを登録し、VCC/ALCOMから同名パッケージをインストールする。
- Modular Avatar: https://modular-avatar.nadena.dev/ 上記Prefabulousを使う時は必須
- PhysBone→DynamicBone変換（任意）: https://github.com/FACS01-01/PhysBone-to-DynamicBone
- DynamicBoneを買ってないけど変換したい場合（任意）: https://github.com/VRLabs/Dynamic-Bones-Stub

### 0. PhysBonesをDynamicBonesに変換（任意）

とりあえず試すだけならいったんやらなくてもいいです。

以下のツールを使ってください：

- https://github.com/FACS01-01/PhysBone-to-DynamicBone

変換するだけならばDynamicBonesを買う必要はありません。代替ツールがあります。

- https://github.com/VRLabs/Dynamic-Bones-Stub
- https://github.com/Markcreator/VRChat-Tools

**揺らさない子ボーンがある場合は？**

CVRのDynamicBoneは古いバージョンであるためか、Root直下のボーンにはExclusion（PBのignore指定）相当がききません。

これを解決するために [ExcludeChildBones](https://github.com/Narazaka/ExcludeChildBones) というツールを作ったので、場合によって使って下さい。（Modular Avatar(NDMF)によって動作するので、このコンポーネントを付けて設定してから改めてベイクする）

### 1. 変換

#### Modular Avatar導入済みの場合

1. Unity 2022.3.22f1 / VRChat SDK 3.x（VCCを使用）でVRChatアバターをセットアップします。
2. （任意）[PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone)などを使用してPhysBonesをDynamicBonesに変換しておきます。
3. ChilloutVR CCK4 Preview をVRChatアバタープロジェクトにインポートします。
4. VRC3CVRの`.unitypackage`をインポートします。
5. アバター直下に`PA-Conversions VRC Constraints -> Unity Constraints`コンポーネントを付けます。
6. アバター直下にVRC3CVRコンポーネントを付けて、Manual bakeします。（`Tools -> Modular Avatar -> Manual bake avatar`）

#### Modular Avatarが無い場合（VRC Constraintsは変換されません！）

1. Unity 2022.3.22f1 / VRChat SDK 3.x（VCCを使用）でVRChatアバターをセットアップします。
2. （任意）[PhysBone-to-DynamicBone](https://github.com/FACS01-01/PhysBone-to-DynamicBone)などを使用してPhysBonesをDynamicBonesに変換しておきます。
3. ChilloutVR CCK4 Preview をVRChatアバタープロジェクトにインポートします。
4. VRC3CVRの`.unitypackage`をインポートします。
5. Tools -> VRC3CVR メニューでツールウインドウを出します。
6. 変換したいVRCアバターを選択します。
    - Modular Avatarやその他のアバタービルドツールを使っている場合は、先に「ベイク」を行ってください（例：`Tools -> Modular Avatar -> Manual bake avatar`）。
7. 「Convert」をクリックします。

### 2. アップロード

変換後のアバターを普通にアップロード。

## Tips

### アップロード数制限

デフォルトで20個くらいしか枠がありません。

課金すると増えるらしい？

### 日本語化

ChilloutVRのデフォルトUIでは非ASCII文字（日本語等）が文字化けしてしまいます。

日本語で使う場合は、[日本語化パッチ](https://github.com/Narazaka/chilloutvr-jp-translation-tool)を導入して下さい。

### CVRがVRCより不便

CVRでは不便はMODでどうにかするスタイルです。

https://github.com/knah/CVRMelonAssistant 等を使ってMODを導入しましょう。

（CVRはもともとVRCがMOD禁止になったのに反発する流れの中で作られたプラットフォームです）

## トラブルシューティング

### "VRCExpressionParameters.Parameter does not contain a definition for defaultValue"（`VRCExpressionParameters.Parameter` に `defaultValue` が存在しない）等のエラー

VRChat Avatar SDK3の最新版にアップデートしてください。

### ジェスチャーで手が動かない

「My avatar has custom hand animations」のチェックを外してから変換してください。

### "The type or namespace 'VRC' could not be found"（`VRC`という型や名前空間が見つからない）エラー

プロジェクトにVRCSDKが含まれていない可能性があります。SDKをインポートしてください。

## 変換の詳細

### ジェスチャーマッピング

| ジェスチャー       | VRC | CVR |
|--------------------|-----|-----|
| なし               | 0   | 0   |
| グー（Fist）       | 1   | 1   |
| パー（Open Hand）  | 2   | -1  |
| 指差し（Point）    | 3   | 4   |
| ピース（Peace）    | 4   | 5   |
| ロック（Rock'n'Roll） | 5 | 6   |
| 銃（Gun）          | 6   | 3   |
| 親指立て（Thumbs） | 7   | 2   |

#### トリガーウェイトの扱い

VRCでは `GestureLeftWeight` と `GestureRightWeight` を使用しますが、CVRではこれらは存在しません。  
代わりに `GestureLeft` の値でトリガー強度を確認します（0.5が50%相当）。
