# AGENTS.md

## 提交与推送

- 每完成一个阶段就提交并推送。
- 推送失败时（例如 SSH 凭据不可用），至少先做本地提交，不要把改动堆在工作区。

## 提交范围

- 只提交与工程本身相关的内容。
- 设计文档、Python 脚本、Blender 工程文件等不提交；AGENTS.md 是唯一的文档类例外。

## 3D 建模

- 3D 建模走 Blender MCP，在 Blender 中制作后导入 Godot。

## 开发方式

- 开发工作尽量在 Godot 内完成，不引入外部素材与资源。
- 确有必要时可以使用本机 Python 和 C++，也可以下载外部库。

## 工程环境

- Godot 4.7 + C#
- Jolt Physics
- D3D12
- mobile 渲染器

## 设计依据

- 设计依据是 `docs/star.md`，本地维护、不提交。
