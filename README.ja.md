# VRC3CVR

VRChat SDK3のアバターをChilloutVR用に変換します。

- 元のプロジェクトのバックアップを必ず取ってください。
- 本プロジェクトは、元のプロジェクト（https://github.com/imagitama/vrc3cvr）をフォークしたプロジェクト（https://github.com/SaracenOne/vrc3cvr）をさらにフォークしたものです。
- Locomotionレイヤーの変換は非推奨です。FXとGestureレイヤーのみの変換を推奨します。

## 動作確認済み環境

- VRChat Avatar SDK3 3.7.x  
- ChilloutVR CCK 3.13.4～3.15.x  
- Unity 2022.3.22f1（VRChat SDK互換のバージョン）

## 使い方

### 1. 変換

[Releases](https://github.com/Narazaka/vrc3cvr/releases/latest)にアクセスし、「Assets」を展開して`.unitypackage`をダウンロードします。

1. Unity 2022.3.22f1 / VRChat SDK 3.x（VCCを使用）でVRChatアバターをセットアップします。
2. （任意）[PhysBone-Converter](https://github.com/Dreadrith/PhysBone-Converter)などを使用してPhysBonesをDynamicBonesに変換しておきます。
3. [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/)をVRChatアバタープロジェクトにインポートします（Unityバージョンの不一致は無視してOK）。
4. VRC3CVRの`.unitypackage`をインポートします。
5. Tools -> VRC3CVR メニューでツールウインドウを出します。
6. 変換したいVRCアバターを選択します。
    - Modular Avatarやその他のアバタービルドツールを使っている場合は、先に「ベイク」を行ってください（例：`Tools -> Modular Avatar -> Manual bake avatar`）。
7. 「Convert」をクリックします。

#### PhysBonesをDynamicBonesに変換するには？

以下のツールを使ってください：

- ~~https://booth.pm/ja/items/4032295~~
- https://github.com/Dreadrith/PhysBone-Converter

変換するだけならばDynamicBonesを買う必要はありません。代替ツールがあります。

- https://github.com/VRLabs/Dynamic-Bones-Stub
- https://github.com/Markcreator/VRChat-Tools  

### 2. エクスポート

VRChatはUnity 2022.3.22f1、CCKはUnity 2021.3.45f1が必要です。

したがって、変換したアバターを2022から2021に移動する必要があります。

1. 変換したアバターをProjectタブにドラッグ＆ドロップしてPrefabを作成します。
2. Prefabを右クリックして「Export Package...」を選びます。
3. アバターの`.unitypackage`を好きな場所に保存します。

ただし…

「Export Package...」は不要なスクリプトアセットを含んでしまう問題があります。 そのため、スクリプトなどを除外してエクスポートできる拡張を作成しました。
[Export Package (Advanced)](https://github.com/Narazaka/ExportPackageAdvanced)をインストールし、「Export Package...」の代わりに「Export Package (Advanced)...」を使うと便利です。

**2022のままでもアップロード可能？**

実はUnity 2022からでも一応CCKでアバターアップロードは可能です。

が、

- だいたいのシェーダーでVRだと片目しか映らない
- たまにCVRがクラッシュするアバターが出来る（起動不可になった場合は [Webのアバター一覧](https://hub.abinteractive.net/myavatars) から何か他のアバターを選んで`Set Active`ボタンを押して切り替えると良い）

等の問題があります。

配信カメラ等には正常に描画されるので、デスクトップでしかプレイしない・VR片目描画でも大丈夫と言う場合はもしかしたら2022そのままアップロードで良いかも……。

### 3. アップロード

1. Unity 2021.3.45f1プロジェクトをセットアップ（CCK互換のバージョン）。
2. [ChilloutVR CCK](https://docs.abinteractive.net/cck/setup/)をインポート。
3. 「Add Existing Project」ボタンでこのプロジェクトをVCCに追加してください。VCCから必要なパッケージをインストールできるようになります。（Unityバージョンの不一致は無視してOK）
4. ConstraintのIsActiveが壊れてしまう問題を修正するために、[under-2022-constraint-activator](https://github.com/Narazaka/under-2022-constraint-activator/releases)をインストールして下さい。（installerと書いてあるものをインポート）
5. アバターが依存しているアセット（シェーダーなど）をインポート。
6. エクスポートしたアバターの`.unitypackage`をインポート。
7. 通常どおりアップロード。

## Tips

ChilloutVRのデフォルトUIでは非ASCII文字（日本語等）が文字化けしてしまいます。

日本語で使う場合は、 https://x.com/DarkFox3150/status/1560974358855569408 にある日本語化パッチを導入して下さい。

## 変換で行われること

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
  - `GestureLeftWeight` / `GestureRightWeight` を `GestureLeft` / `GestureRight` に変換  
  - VRCParameterDriverなども変換
- VRC Contact SenderとReceiverをCVR PointerとCVR Advanced Avatar Triggerに変換
  - VRCContactと違って、CVR PointerやTriggerはContactが衝突した時にしか値を変更しません。この差異によって互換性の問題を生じる可能性があります。
  - プレイ中のContactのShape Typeの変更は非対応です。

## ジェスチャーマッピング

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

### トリガーウェイトの扱い

VRCでは `GestureLeftWeight` と `GestureRightWeight` を使用しますが、CVRではこれらは存在しません。  
代わりに `GestureLeft` の値でトリガー強度を確認します（0.5が50%相当）。

## トラブルシューティング

### `VRCExpressionParameters.Parameter` に `defaultValue` が存在しない等のエラー

VRChat Avatar SDK3の最新版にアップデートしてください。

### ジェスチャーで手が動かない

「My avatar has custom hand animations」のチェックを外してから変換してください。

### `VRC`という型や名前空間が見つからない

プロジェクトにVRCSDKが含まれていない可能性があります。SDKをインポートしてください。
