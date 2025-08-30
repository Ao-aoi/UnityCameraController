#CamCtrl Usage Guide

日本語

##概要:
CamCtrlは、画面端の指定エリアにマウスカーソルを置くと、カメラが加速・減速しながら移動・回転する直感的な操作を実現するUnity用MonoBehaviourです。

##使い方:
1. シーン内のメインカメラにCamCtrlスクリプトをアタッチします。

2. インスペクターで「移動軸（Movement Axis）」と「移動範囲長（Axis Range Length）」を設定します。初期位置も範囲内で指定できます。

3. スクリプトが自動的に4つのエッジエリア（左・右・上・下）をCanvas上に生成します。各エリアの幅・高さの割合はインスペクターで調整可能です。

4. 操作エリアにマウスカーソルを合わせると、その方向にカメラが移動または回転します。カーソルを外すとカメラは減速して停止します。

##オプション:
ヘッドボブ（カメラの揺れ）を有効/無効にできます。
エッジエリアの表示/非表示を切り替えられます。
加速度・最大速度・減衰率なども好みに合わせて調整できます。

English
##Overview:
CamCtrl is a Unity MonoBehaviour that enables intuitive camera movement and rotation by simply hovering the mouse cursor over designated edge areas of the screen. The camera smoothly accelerates and decelerates in the direction of the hovered area, providing a user-friendly control experience.

##How to Use:

Attach to Camera:
Add the CamCtrl script to your Camera GameObject in the Unity scene.

##Set Movement Range:
In the Inspector, configure the movement axis (Movement Axis) and the range (Axis Range Length) for how far the camera can move. You can also set the initial position within this range.

##Configure Edge Areas:
The script automatically creates four edge areas (left, right, top, bottom) as UI elements on your Canvas. Adjust the width/height percentages for each edge in the Inspector to control their size.

##Control the Camera:
When you move the mouse cursor over one of the edge areas, the camera will smoothly move or rotate in the corresponding direction. Moving the cursor away will slow and stop the camera.

##Optional Settings:

Enable or disable head bobbing (camera shake effect).
Show or hide the edge areas for visual feedback.
Adjust acceleration, speed, and damping parameters for custom feel.
