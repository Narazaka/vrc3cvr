# 3.0.0-rc.2

- **BREAKING**: NDMFプラグイン経路は廃止されました。`Tools -> Modular Avatar -> Manual bake avatar` では変換されなくなりました。`VRC3CVRNDMF` コンポーネントは `VRC3CVR Avatar` になり、設定は引き継がれます
- 古いバージョンから更新すると `Assets/PeanutTools/VRC3CVR/NDMF/` フォルダが残ることがあります。削除して問題ありません
- feat: CCK Control Panel でのアップロード時に、アバターが変換されるようになりました。`VRC3CVR Avatar` コンポーネント（CCKが必要とする `CVRAvatar` と `CVRAssetInfo` も一緒に付きます）を付けてアップロードを押すだけです。VRChatのアップロード時にModular AvatarやVRCFuryが動くのと同じで、変換の手順を別に踏む必要はもうありません
- feat: 非破壊系ツール（VRCFury、Modular Avatar、Avatar Optimizerなど）が変換前にベイクされるようになり、自分で先にベイクする必要が無くなりました（`Auto bake`、既定でON）
- **手動**変換で `Auto bake` が働いた場合、できあがったアバターは一時フォルダにある生成アセットを参照しており、次のビルドで失われます。保存して使うのではなく、そのままアップロードして下さい。CCK Control Panel からのアップロードには影響しません
- feat: `VRC3CVR Avatar` のインスペクタからもアバターを変換できるようになりました。`Tools -> VRC3CVR` ウインドウとインスペクタは同じ設定を編集するので1つのアバターに設定が2つ存在することはなく、ウインドウ側の設定をそのまま `VRC3CVR Avatar` コンポーネントとしてアバターに保存できます
- feat: VRC ConstraintsをUnity Constraintsに変換するようになりました（`Convert VRC Constraints`、既定でON）。Constraintの変換にPrefabulousは不要になりました。Constraintのプロパティを動かしているアニメーションクリップもUnity側の対応するプロパティに繋ぎ直され、Target Transformが設定されたConstraintはそのTransformのGameObjectへ移動します。Unityに対応の無いVRC固有の機能（`FreezeToWorld`、`SolveInLocalSpace`）は警告を出して破棄されます
- feat: `VelocityMagnitude` を供給するようになりました。ChilloutVRには相当するものが無いため、クライアントが元から供給している `VelocityX/Y/Z` から各クライアントで再計算します。同期の枠は消費しません
- feat: `MuteSelf`・`VRMode`・`Upright` をゲームから供給するようになりました（`Feed MuteSelf / VRMode / Upright`、既定でON）。これらは同期パラメータとして宣言されるため、使用しているアバターでは同期の枠を消費します
- feat: `GestureLeftWeight` / `GestureRightWeight` の変換方式を選べるようになりました（`GestureLeftWeight/GestureRightWeightの変換方式`）。`GestureLeft` / `GestureRight` への書き換えが引き続き既定で、weight条件とweight駆動の1Dブレンドツリーについては、VRChatの「Fist以外では1固定」という挙動も再現するようになりました。新しい方式はweightパラメータを残して `GestureLeft` から供給するもので、motion timeステートや2Dブレンドツリーにも対応しますが、1フレームの遅延が生じます
- feat: `GestureLeft` / `GestureRight` に対する `Greater` / `Less` 条件が、破棄されるのではなく変換されるようになりました。VRChatとChilloutVRではジェスチャーの番号付けが異なるため、各比較は該当するジェスチャーごとに1つのtransitionへ展開されます
- fix: transitionの条件がパラメータの実際の型に一致するようになりました。`IsLocal` のような組み込みのboolをblend treeの都合でFloatとして宣言しているアバターでは `uses parameter '...' which is not compatible with condition type` が発生し、そのレイヤーが動作しなくなっていました。実際の型では等価に表現できない条件は、警告を出して破棄します。残すとレイヤー全体が動かなくなるためです
- fix: ブレンドツリーの中に入れ子になったブレンドツリーも、`GestureLeftWeight` / `GestureRightWeight` で駆動されている場合は変換されるようになりました。以前はステート直下の最上位ツリーしか書き換えられず、入れ子のツリーはChilloutVRが一切駆動しないパラメータに繋がったまま残り、常にしきい値が最小の子が再生されていました
- fix: サブステートマシンから出ていくtransitionも変換されるようになりました。これらは親のステートマシン側に保存されているため、見落とされていました
- fix: マージされたVRC animatorの最初のレイヤーが無効化されなくなりました。Unityはシリアライズされたweightに関わらずlayer 0を常にweight 1で実行するため、マージによってそのレイヤーが最初の位置から動く前に、そのweightを焼き込むようになりました
- fix: expression parametersの無いアバターが、変換途中で例外を投げて中途半端なアバターを残すのではなく、正常に変換されるようになりました
- fix: アバターマスクの結合で、特定の非ヒューマノイドtransformに対する制限が失われなくなりました。レイヤーから除外されていたpropやボーンが、変換後に再びアニメーションしてしまう問題でした
- fix: 負のトグル値がintドロップダウンから消えなくなりました
- fix: intドロップダウンの選択肢が、共通の親サブメニューを持たない場合に名前の先頭1文字を失わなくなりました
- fix: puppetのサブパラメータとしてのみ使われるIntパラメータの変換で `InvalidOperationException` が発生して変換が中断することがなくなりました。animatorパラメータ自体は引き続き変換され、CVRメニューの項目のみが警告付きで省略されます
- fix: puppetの「changing」パラメータが値1のトグルにも使われている場合に `ArgumentException` が発生して変換が中断することがなくなりました
- fix: VRCのstate machine behaviourを変換後に削除するようになり、変換後のコントローラに壊れたスクリプト参照が残らなくなりました
- fix: 組み込みのアバターマスクが、ディレクトリの実際の大文字小文字を使って読み込まれるようになりました。大文字小文字を区別するファイルシステムでは、すべてのマスクがnullとして読み込まれ、何もログに出ないままレイヤーがヒューマノイドリグ全体に対してマスク無しで実行されていました
- fix: つま先ボーンが無い場合のエラーを削除しました。アップロード前に直すべきものとして表示されていましたが、ChilloutVRはつま先ボーンを必要としません

# 3.0.0-rc.1

- fix: nullチェック / 軽微な不具合の修正

# 3.0.0-rc.0

- feat: CCK4向けドキュメント
- fix: VRCConstraints変換（Prefabulous）についてのドキュメント
- feat: CCK4の既定のAuto-Generated Avatar Pointersに対応
- fix: VRC3CVRコンポーネントのContact変換設定が反映されていなかった問題を修正

# 3.0.0-beta.13

- fix: CCK_4.0.0-Preview.25以降に合わせて LessThen -> LessThan に変更

# 3.0.0-beta.12

- fix: ドキュメント

# 3.0.0-beta.11

- fix: DynamicBone階層下のオブジェクトにContactsが存在すると、DynamicBoneが動作しなくなる問題を修正
- feat: Contactのenabledアニメーションが、GameObjectのactive状態へ正しく変換されるようになりました

# 3.0.0-beta.10

- コンポーネント内のパスごとのCollision Tag変換設定が正しく機能しない問題を修正
- VRC Avatar Parameter Driverの不正な範囲変換を無視するように
- Viseme用BlendShapeが無い場合にエラーが出ないように
- 変換処理が途中で止まったままになる問題を防止
- リファクタリング

# 3.0.0-beta.9

- fix: 両方のつま先ボーンが無い場合にエラーが1つしか表示されない問題を修正
- fix: visemeとまばたきのblendshapeに対するエラーチェック漏れを修正
- fix: 変換中に再度変換を実行できてしまう問題を修正
- fix: コード整理

# 3.0.0-beta.8

- feat: VRC Head Chopを変換（Scaleが0または1の場合のみ）
- feat: VRC Spatial Audio Sourceを変換（実験的機能: gainやdistanceの値が正しく変換される保証はありません）

# 3.0.0-beta.7

- fix: VRC Avatar Descriptorのcollider設定に内部的に不正なデータがある場合に変換が失敗する問題を、おそらく修正

# 3.0.0-beta.6

- feat: 変換後のContactsがリモートでも動作するように

# 3.0.0-beta.5

- fix: state paramsの変換

# 3.0.0-beta.4

- fix: VRCEmote => Emoteの変換をやめる（revert）

# 3.0.0-beta.3

- feat: パラメータ互換性の改善
  - 以下のパラメータが対応するCVRのパラメータに置き換えられるようになりました:
    - VRCEmote => Emote
    - Viseme => VisemeIdx
    - Voice => VisemeLoudness
    - Seated => Sitting
    - InStation => Sitting
    - IsOnFriendsList => IsFriend
  - 以下のパラメータの初期値が1に設定されるようになりました:
    - ScaleFactor
    - ScaleFactorInverse
    - EyeHeightAsPercent

# 3.0.0-beta.2

- feat: CCK_4.0.0_Preview.19に対応！

# 3.0.0-beta.1

- fix: PB変換ツールのURL
- chore(breaking): NDMF>=1.8

# 3.0.0-beta.0

- feat: NDMFプラグイン
- feat: タグ変換で親のコンポーネントも考慮されるように
- feat: パスごとのタグ変換設定
- feat: UI改善

# 2.2.0

- feat: Action Menu modの「impulse」アノテーション
- feat: パラメータの同期状態を保持
- fix: メニューに余分なパラメータを追加しないように
- fix: BlendTree / AnimationClipのコピー

# 2.2.0-beta.2

- fix: ヒューマノイドアニメーションの変換

# 2.2.0-beta.1

- feat: Action Menu modの「impulse」アノテーション
- feat: パラメータの同期状態を保持
- fix: メニューに余分なパラメータを追加しないように
- fix: BlendTree / AnimationClipのコピー

# 2.1.0

- feat: Action Menu modの「hidden」アノテーション

# 2.0.0

- **CCK 3.15.xに対応！**
- feat: VRCメニュー順に対応
- feat: メニュー名検出の改善
- feat: 階層的なメニュー名
- feat: VRCParameterDriverの変換
- feat: VRCAnimatorLocomotionControlの変換
- feat: VRCAnimatorTrackingControlの変換（部分対応: 目・指・口を除く）
- feat: VRC Contactsの変換
  - 新規: VRC3CVRCollisionTagConvertionコンポーネント（VRCContactsと同じオブジェクトにアタッチ）
- feat: Groundedパラメータを既定でtrueに（プレビュー時に便利）
- feat: 自動化のため一部のメソッド・フィールドをpublicに
- ui: メニューを「PeanutTools/VRC3CVR」から一般的な「Tools/VRC3CVR」に移動
- ui: GUI刷新
- ui: ja-JPローカライズ
- fix: Modular Avatarと併用できるようAnimator Controller生成を修正
- fix: animatorの「name」

# 2.0.0-rc.17

- feat: ドロップダウンメニュー名
- feat: 階層的なメニュー名

# 2.0.0-rc.16

- feat: position/rotation/radius/heightのcontactアニメーション再マッピング

# 2.0.0-rc.15

- fix: contactsのlocalOnlyを変換

# 2.0.0-rc.14

- feat: contactsのlocalOnlyを変換

# 2.0.0-rc.13

- feat: VRC Contactsの変換
  - 新規: VRC3CVRCollisionTagConvertionコンポーネント（VRCContactsと同じオブジェクトにアタッチ）
- fix: animator専用パラメータの初期値が0にクリアされてしまう問題を修正

# 2.0.0-rc.12

- feat: Groundedパラメータを既定でtrueに（プレビュー時に便利）

# 2.0.0-rc.11

- feat: 自動化のため一部のメソッド・フィールドをpublicに

# 2.0.0-rc.10

- fix: 一部のアバターでの変換エラー

# 2.0.0-rc.9

- feat: VRCParameterDriverの変換
- feat: VRCAnimatorTrackingControlの変換（部分対応: 目・指・口を除く）
- feat: VRCAnimatorLocomotionControlの変換

# 2.0.0-rc.8

- fix: ステートマシン間のtransitionがコピーされない問題を修正（複雑なanimatorで発生）

# 2.0.0-rc.7

- feat: VRCメニュー順に対応

# 2.0.0-rc.6

- Bool/Floatのメニュー名検出
- GUI刷新
- ja-JPローカライズ

# 2.0.0-rc.5

- 保存の修正

# 2.0.0-rc.4

- ステートマシン保存の修正

# 2.0.0-rc.3

- リリースの修正

# 2.0.0-rc.2

- animatorの「name」を修正

# 2.0.0-rc.1

- MAと併用できるようAnimator Controller生成を修正
- メニューを「PeanutTools/VRC3CVR」から一般的な「Tools/VRC3CVR」に移動

# 2.0.0-rc.0

- CCK 3.13.4に対応

# 1.2.6S

- blend treeのYパラメータ命名を修正

# 1.2.5S

- mainブランチへリベース
- ボイス位置のスケーリングを修正
- hand idleとfist間のしきい値生成を修正
- 空のblend tree motionでnullエラーが出ないように

# 1.2.4S

- 5つのVRChatベースanimatorそれぞれを変換するか無視するか選べるトグルを、説明付きで追加
- ボイス位置が、目の位置ではなく頭ボーンの根本（見つかった場合）に配置されるように
- アバターがシーンのルートに置かれている場合の顔メッシュの割り当てを修正

# 1.2.3S

- VRC3CVR_Ouputディレクトリが作成されないバグを修正

# 1.2.2S

- 全animatorでのanimatorマスキング対応を改善

# 1.2.1S

- VRC ExpressionMenuの無いアバターでのエラーに対処するホットフィックス

# 1.2.0S

- パラメータ名に関するCVRの制限に合わせる
- VRCコンポーネントの削除を任意に
- 各animatorの最初のレイヤーのweightを修正
- FXレイヤーに空のマスキングを追加
- VRCメニューから正しいintegerパラメータ名を取得
- 正しいマスキングとproxyアニメーション付きでgesture animatorを変換できるように対応を追加

# 1.1.1

- 顔メッシュが古いメッシュを使ってしまう問題を修正

# 1.1.0

- VRCコンポーネントをすべて正しく削除するように
- skinned mesh rendererの無いアバターの変換を修正
- 警告を正しくログ出力するように
- クローントグルを追加

# 1.0.3

- パラメータの型をfloatに上書きしないように

# 1.0.2

- クラッシュを修正

# 1.0.1

- visemeが検出されない場合を無視するように

# 1.0.0

- githubリポジトリ名に合わせて「vrc3cvr」に改名
- 最新のVRCSDKとCCKに合わせて更新
- UIを改善
- null参照エラーを修正（[issue 9](https://github.com/imagitama/vrc3cvr/issues/9)）
- 元のアバターを保持するためクローンするように
- PhysBonesの変換についてのメッセージを追加

# 0.0.12

- github issue #8のため追加のログ出力を実装

# 0.0.11

- timeパラメータとblend treeで `GestureLeftWeight`/`GestureRightWeight` の代わりに `GestureLeft`/`GestureRight` を使うように変更
- まばたきblendshapeが無い場合のクラッシュを修正

# 0.0.10

- 左右のつま先ボーンが未設定の場合に出力するように

# 0.0.9

- CVR提供の `LeftHand` / `RightHand` レイヤーを削除するかどうかを決めるチェックボックスを追加

# 0.0.8

- ドロップダウン項目が1つだけの場合はドロップダウンの代わりにトグルを表示

# 0.0.7

- 安静時のジェスチャーがopen-hand/surprisedジェスチャーとして表示される問題を修正

# 0.0.6

- `NotEqual` のint条件がfloatに正しく変換されない問題を修正

# 0.0.5

- intのVRCパラメータを使う条件が無い場合はドロップダウンを表示しないように

# 0.0.4

- intのVRCパラメータ用ドロップダウン

# 0.0.3

- booleanパラメータにトグル（Game Object Toggles）を使用

# 0.0.2

- レイヤー名の重複によりanimator controllerが動作しない問題を修正
- スライダーに戻した
- `NotEqual` 条件をfloat値の `LessThan` に変更

# 0.0.1

初回リリース。
