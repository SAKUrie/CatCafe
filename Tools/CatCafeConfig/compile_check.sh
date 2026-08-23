#!/usr/bin/env bash
# 不开 Unity 也能编译 C#，拿到真实的 error CS 列表。
#
# 原理：Unity 每次编译都会把完整的编译参数（宏定义、引用程序集、源文件清单）
# 写进 Library/Bee/artifacts/<dag>/Assembly-CSharp.rsp，直接喂给 Unity 自带的
# Roslyn 就能复现同一次编译。比"数括号配平"靠谱得多。
#
# 前提：项目在 Unity 里至少成功编译过一次（rsp 才存在）。改了 asmdef 或加了新
# 程序集之后 rsp 会过期，回 Unity 编一次即可刷新。
#
# 用法：bash Tools/CatCafeConfig/compile_check.sh
set -u

UNITY_ROOT="${UNITY_ROOT:-/c/Program Files/Unity/Hub/Editor/6000.0.60f1/Editor/Data}"
DOTNET="$UNITY_ROOT/NetCoreRuntime/dotnet.exe"
CSC="$UNITY_ROOT/DotNetSdkRoslyn/csc.dll"

if [ ! -f "$CSC" ]; then
    echo "找不到 Unity 自带的 Roslyn：$CSC"
    echo "改用别的 Unity 版本时设环境变量：UNITY_ROOT=<...>/Editor/Data $0"
    exit 2
fi

# 取最新的编辑器 dag（E 结尾的是 Editor 平台那一套）
RSP_DIR=$(ls -dt Library/Bee/artifacts/*E.dag 2>/dev/null | head -1)
if [ -z "$RSP_DIR" ]; then
    echo "找不到 Library/Bee/artifacts/*E.dag —— 先在 Unity 里成功编译一次。"
    exit 2
fi

# 防呆：rsp 里是 Unity 上次编译时的源文件清单。新加的 .cs 在 Unity 刷新之前
# 不在清单里，csc 根本看不到它——于是这个脚本会对一个有语法错误的新文件报「通过」。
# 真出过这个假阳性，所以先把「没进任何 rsp 的 .cs」列出来。
uncompiled=""
while IFS= read -r cs; do
    rel="${cs#./}"
    if ! grep -qF "\"$rel\"" "$RSP_DIR"/*.rsp 2>/dev/null; then
        uncompiled="$uncompiled  $rel\n"
    fi
done < <(find Assets -name '*.cs' -not -path '*/Library/*' | sed 's|^|./|')

if [ -n "$uncompiled" ]; then
    echo "⚠ 以下 .cs 不在任何 rsp 清单里，本次检查覆盖不到它们："
    printf "$uncompiled"
    echo "  （回 Unity 让它刷新一次编译，再跑本脚本）"
    echo
fi

status=0
for asm in Assembly-CSharp Assembly-CSharp-Editor; do
    rsp="$RSP_DIR/$asm.rsp"
    if [ ! -f "$rsp" ]; then
        echo "跳过 $asm（没有 $rsp）"
        continue
    fi
    echo "── $asm ──"
    out=$("$DOTNET" "$CSC" "@$rsp" -nologo 2>&1)
    errors=$(echo "$out" | grep -E "error [A-Z]+[0-9]+" || true)
    if [ -n "$errors" ]; then
        echo "$errors"
        status=1
    else
        echo "  通过"
    fi
done

exit $status
