// IMP-CAM-001: 吸着表示 (構図合わせ不要の持ち帰り)
//
// KanjiFind KF_PhotoPrint.shader の移植 (ロジック無改造 — 実機ベータで
// 吸着 0.8m / ミラー除外 / 非反転 / contain 補正を確認済みの実装)。
// 原設計の正本: KanjiFind SYS-WD-01 §6。
//
// **板を動かすのではなく、写真カメラのパスでのみ板の描画位置を差し替える。**
// VRChat のカメラはプレイヤーが持つもので姿勢を Udon から取れないため、
// 「カメラに合わせて板を動かす」方式は原理的に取れない。一方シェーダーは
// 「今どのカメラがこのパスを描いているか」を知れるので、描画側で解決する。
//
// **吸着板を複数置くときはエイム判定を有効にする** (IMP-GAL-002)。
// 原設計 (SYS-WD-01 §6.5) の「1 枚だけ」制約は判定が「カメラが距離内か」だけ
// だったため。エイム判定が加わることで、吸着圏が重なっていても
// カメラが狙っている 1 枚だけが全画面化する。
//
// **エイム判定は「視線が板の面のどこに当たるか」で見る** (BUG-GAL-001)。
// 旧実装は「板の中心を ±15° 以内で狙っているか」という**角度**だったため、
// 近づくほど許される横ずれが縮み、**板にくっつけると 1cm のずれで外れた**
// (0.05m で ±1.3cm)。VRChat では「近づける = 確実に撮れる」が共通作法なので、
// 近距離で効かないのは初見が詰まる形になる。
//
// 視線と板の平面の交点が **板の矩形 + 余白**に入っているかで判定すると:
//   - 距離に依存しない (くっつけても、腕を伸ばした距離でも同じ許容量)
//   - 近距離ほど角度の許容が自然に広がる (5cm なら 30° 傾けても交点は板の中)
//   - **板はすべて同一平面**なので交点はただ 1 点 = 同時に 2 枚は成立しない。
//     隣接板との排他が「調整した円錐」ではなく**幾何的な保証**になる
Shader "KominkaDogRun/KD_PhotoPrint"
{
    Properties
    {
        _MainTex ("Photo", 2D) = "black" {}
        // 吸着距離。持ち帰り台は 1 箇所だけなので短く詰められる
        _AttachDistance ("Attach Distance (m)", Float) = 0.8
        // 視線が当たってよい範囲を板の縁から何 m 広げるか (BUG-GAL-001)。
        // 隣接板と重ならない値にすること (= 板の隙間の半分より小さく)。
        // **負でエイム判定を無効化** (単独設置の台は距離だけで吸着する従来挙動)
        _AttachMargin ("Aim Margin (m, negative = disabled)", Float) = -1
        // 1 にすると通常の目線では描かず、写真カメラの中にだけ出す (逃げ道)
        _HideFromEyes ("Hide From Eyes", Float) = 0
        // **切り分け用。既定は 0 で、製品ビルドでは常に 0** (BUG-CAM-003)。
        // 1 にすると全画面化したときに 4 本の帯を出す (下の frag を参照)。
        // 実機でしか再現しない黒画面を **1 回のアップロードで切り分ける**ために置いてある。
        // 切り替えは Tools/KominkaDogRun/Photo System/4 - ON / 5 - OFF。
        // ON のまま公開するのは Release Check/1 が FAIL で止める
        _DebugProbe ("Debug Probe (0/1)", Float) = 0
    }

    SubShader
    {
        // **バッチングを禁止する。** 頂点を元の座標系のまま扱うため、
        // まとめられるとワールド変換が焼き込まれて座標が壊れる
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _AttachDistance;
            float _AttachMargin;
            float _HideFromEyes;
            float _DebugProbe;

            // VRChat が全マテリアルへ供給するグローバル値
            float _VRChatCameraMode;   // 0 = 通常の目線 / それ以外 = 写真カメラ等
            float _VRChatMirrorMode;   // 0 以外 = ミラー描画

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                // 全画面化しているパスかをフラグメントへ渡す (範囲外の塗り潰しに使う)
                float fullscreen : TEXCOORD1;
                // **加工していないメッシュの UV** (BUG-CAM-003 のプローブ用)。
                // 疑っている当の `uv` で帯を割ると何も切り分けられないので、
                // **確実に 0..1 である値**を別に持つ
                float2 raw : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.raw = v.uv;

                // --- 吸着してよいパスかの判定 ---
                // ミラー除外は必須。忘れると鏡に写った瞬間に全画面化する
                float3 boardOrigin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float camDistance = distance(_WorldSpaceCameraPos, boardOrigin);
                bool isPhotoCamera = _VRChatCameraMode > 0.5;
                bool isMirror = _VRChatMirrorMode > 0.5;

                // --- エイム判定 (IMP-GAL-002 / 判定方式は BUG-GAL-001 で改訂) ---
                // 「視線が板の平面に当たる点」が板の矩形 + 余白に入っているか。
                //
                // 板のワールド軸は行列の列から取る。Quad の頂点は ±0.5 なので
                // 列の長さがそのまま板のワールド寸法になる (半分が半径)。
                // **前提: 板に shear が無いこと** (= 親のスケールが一様であること)。
                // 回転した親に非一様スケールが載ると rightN と upN が直交しなくなり、
                // 軸への射影が矩形座標にならない。ギャラリーの親は一様 0.5 なので成立する。
                float3 axX = float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20);
                float3 axY = float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21);
                float3 axZ = float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22);
                float halfW = 0.5 * length(axX);
                float halfH = 0.5 * length(axY);
                float3 rightN  = axX / max(length(axX), 1e-6);
                float3 upN     = axY / max(length(axY), 1e-6);
                float3 normalN = axZ / max(length(axZ), 1e-6);

                // ビュー行列の第 3 行はカメラの後方ベクトル (ワールド系) なので符号反転で前方
                float3 camForward = -UNITY_MATRIX_V._m20_m21_m22;
                float3 toCam = _WorldSpaceCameraPos - boardOrigin;
                float lz = dot(toCam, normalN);       // 板の平面からの符号付き距離
                float fz = dot(camForward, normalN);  // 視線が平面を向いている度合い

                // 交点 hit = toCam + camForward * t,  t = -lz / fz。
                // **除算しない** — 判定式の両辺へ |fz| を掛けて比較する。
                // こうすると視線が平面と平行に近くても inf / NaN を踏まない。
                float2 hitF = float2(
                    dot(toCam, rightN) * fz - lz * dot(camForward, rightN),
                    dot(toCam, upN)    * fz - lz * dot(camForward, upN));
                float absF = abs(fz);
                // 平面がカメラの前方にあること (背後の板を「狙っている」ことにしない)。
                // 板の裏側からでも成立する — くっつけて突き抜けた瞬間に切れないため
                bool planeAhead = lz * fz <= 0.0;
                // **視線が平面と平行なときは成立させない。**
                // fz = 0 だと判定式の両辺が 0 になり (0 <= 0)、
                // **吸着距離内の板が全部同時に成立する** — 排他が壊れる唯一の穴。
                // 「カメラが平面上に居て、かつ視線も平面と平行」という退化ケースで、
                // 板を突き抜けた瞬間に面と平行へ向けると到達しうる (Codex 指摘・高)。
                // 平行 = そもそも写真を見ていないので、落とすのが正しい。
                bool notParallel = absF > 1e-5;
                bool aimOk = _AttachMargin < 0.0
                    || (planeAhead && notParallel
                        && abs(hitF.x) <= (halfW + _AttachMargin) * absF
                        && abs(hitF.y) <= (halfH + _AttachMargin) * absF);

                bool attach = isPhotoCamera && !isMirror && camDistance < _AttachDistance && aimOk;

                if (attach)
                {
                    // --- 画面全体に対応する位置へ頂点を置く ---
                    // Unity の Quad は XY が ±0.5 なので 2 倍すると NDC の ±1 に一致する
                    float2 ndc = v.vertex.xy * 2.0;

                    // 深度は手前へ寄せる。**ただし完全に手前だとクリップされうる**ので
                    // わずかに内側へ入れる。手前の向きは環境で逆になるため両対応する
                #if UNITY_REVERSED_Z
                    float z = UNITY_NEAR_CLIP_VALUE - 1e-4;
                #else
                    float z = UNITY_NEAR_CLIP_VALUE + 1e-4;
                #endif
                    o.pos = float4(ndc, z, 1.0);

                    // --- アスペクト比の補正 ---
                    // 画面と写真の縦横比が違うと伸びるので、UV 側で表示範囲を決め直す。
                    // 収まる側 (contain) に合わせ、はみ出した領域はフラグメントで黒く塗る。
                    //
                    // **供給値が 0 / 非有限なら補正そのものを捨てる** (BUG-CAM-003)。
                    // 旧実装は分母を `max(x, 1.0)` で守っていたが、**守っていたのは分母だけ**で
                    // texAspect が 0 になる経路 (= `_MainTex_TexelSize.z` が 0) が空いていた。
                    // そこから `screenAspect / 0` = inf → `(v.uv - 0.5) * inf` は
                    // **中央の頂点で 0 * inf = NaN** になる。NaN はすべての比較が false なので
                    // frag のはみ出し判定を素通りし、`tex2D(NaN)` の黒が画面全面に出る
                    // = **板は正常なのに全画面化した瞬間だけ真っ黒**になる。
                    //
                    // 補正を捨てた場合の最悪は「縦横比のずれた写真」なので、
                    // **どちらが出るかで供給値が壊れているかを実機で切り分けられる**
                    // (真っ黒のままなら原因はここではない)。
                    // 判定は否定形ではなく肯定形で書く — NaN も 0 も「補正しない」へ落ちる
                    float sw = _ScreenParams.x;
                    float sh = _ScreenParams.y;
                    float tw = _MainTex_TexelSize.z;
                    float th = _MainTex_TexelSize.w;
                    float2 scale = float2(1.0, 1.0);
                    if (sw > 0.0 && sh > 0.0 && tw > 0.0 && th > 0.0)
                    {
                        float screenAspect = sw / sh;
                        float texAspect = tw / th;
                        if (screenAspect > texAspect) scale.x = screenAspect / texAspect;
                        else scale.y = texAspect / screenAspect;
                    }

                    float2 uv = (v.uv - 0.5) * scale + 0.5;

                    // 縦の向きが逆になる環境がある。無対応だと写真が上下反転する
                    if (_ProjectionParams.x < 0) uv.y = 1.0 - uv.y;

                    o.uv = uv;
                    o.fullscreen = 1.0;
                }
                else
                {
                    // 通常の目線では**ただの板として普通に見える**
                    o.pos = UnityObjectToClipPos(v.vertex);
                    o.uv = v.uv;
                    o.fullscreen = 0.0;
                }
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 全画面化中の範囲外は **黒で塗る** (BUG-CAM-003)。
                // 旧実装は discard していたが、それだと**写真の外に世界が透けて**
                // 持ち帰った 1 枚に背景の帯が入る。画面比が 16:9 でない端末
                // (スマホの 19.5:9 等) では必ず出るので、帯は黒で潰す。
                //
                // **比較の向きは変えない。** NaN はすべての比較を false にするので、
                // この向きなら「万一 UV が NaN でも範囲内として写真を出す」に倒れる。
                // 逆向き (`>= 0.0 && <= 1.0` で範囲内と判定) に書くと NaN が
                // **全面黒**へ倒れ、まさに直そうとしている症状を再発させる
                if (i.fullscreen > 0.5)
                {
                    // ================================================================ プローブ
                    // **実機でしか出ない黒画面を 1 回のアップロードで切り分ける** (BUG-CAM-003)。
                    // 帯の区切りには **加工していない `raw`** を使う — 疑っている `uv` で
                    // 割ると、UV が壊れていたとき帯そのものが出ずに何も分からない。
                    //
                    //   左から 1/4 : マゼンタ (定数)      … フラグメントが走っているか
                    //   2 本目     : UV を色にしたもの     … uv が滑らかな勾配か (平坦なら異常値)
                    //   3 本目     : 補正後 UV で引いた写真 … 通常の全画面表示と同じもの
                    //   右 1/4     : **生 UV** で引いた写真 … 補正を疑わずにテクスチャだけ試す
                    //
                    // 3 本目が黒で 4 本目に写真が出れば UV、両方黒ならテクスチャ、
                    // マゼンタすら出なければ「見えている黒は吸着板ではない」
                    if (_DebugProbe > 0.5)
                    {
                        // **サンプルは帯の分岐の外で取る。** tex2D は暗黙の微分を使うので、
                        // 画素ごとに違う条件 (`raw.x`) の分岐の中に置くと、コンパイラによっては
                        // 平坦化されるか弾かれる。`_DebugProbe` は uniform なのでこの階層は安全
                        fixed3 photoFixed = tex2D(_MainTex, i.uv).rgb;
                        fixed3 photoRaw = tex2D(_MainTex, i.raw).rgb;
                        if (i.raw.x < 0.25) return fixed4(1.0, 0.0, 1.0, 1.0);
                        if (i.raw.x < 0.50) return fixed4(saturate(i.uv.x), saturate(i.uv.y), 0.0, 1.0);
                        if (i.raw.x < 0.75) return fixed4(photoFixed, 1.0);
                        return fixed4(photoRaw, 1.0);
                    }

                    if (i.uv.x < 0.0 || i.uv.x > 1.0 || i.uv.y < 0.0 || i.uv.y > 1.0)
                        return fixed4(0.0, 0.0, 0.0, 1.0);
                }
                else if (_HideFromEyes > 0.5)
                {
                    // 掲示物として置きたくない場合の逃げ道
                    discard;
                }
                // **アルファは 1 に固定する。** 不透明として描いているので PC では
                // 捨てられる値だが、モバイルの合成がアルファを見る実装だと
                // 写真の透明度としてそのまま効く (黒く合成されうる)
                return fixed4(tex2D(_MainTex, i.uv).rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
