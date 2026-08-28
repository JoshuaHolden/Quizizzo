# PhaseRing Project Guide

PhaseRing lets this project select one active development phase. The selected phase is injected into Codex through a project-level `UserPromptSubmit` Hook starting with the next user message. Every chat in the same project shares the current selection.

## Select a phase

1. Select `PhaseRing` in the VS Code status bar.
2. Choose the active phase.
3. Send the next user message in Codex.
4. When the status becomes `Active`, PhaseRing has received a real Hook receipt matching the current selection.

First initialization provides five editable phases:

- `00-free.md`: adds no phase-specific boundaries;
- `10-discussion.md`: clarifies goals, requirements, and options;
- `20-planning.md`: turns confirmed goals into an implementation and acceptance plan;
- `30-implementation.md`: implements the confirmed approach and verifies it;
- `40-verification.md`: checks whether the implementation meets its goals.

These phases are starting points, not a fixed workflow. You can edit, rename, add, or remove optional phases for this project.

## Trust the project Hook

Codex requires non-managed Hooks to be reviewed and trusted before they run. Trust only the definition whose command points to this project's `.codex/hooks/phasering.ps1`.

If one approval does not produce `Active`, use the temporary workaround observed on our Windows test machine:

1. Trust the PhaseRing project Hook in the VS Code Codex review screen.
2. Start Codex CLI in this project, run `/hooks`, and trust the same Hook there.
3. If two path variants are listed, including `C:\...` and `c:\...`, make sure both are trusted.
4. Run `Developer: Reload Window` in VS Code, then send another message.

The two-entry behavior is a single-machine observation, not an official Hook requirement. If one approval already produces `Active`, no duplicate entry is needed.

## Customize phases

Add Markdown files directly under `.phasering/modes/`. Each file must:

- use UTF-8 encoding;
- begin with a Markdown H1 as its first non-empty line;
- keep the first body paragraph concise because it appears in the phase picker;
- use numeric prefixes such as `10-` and `20-` to control display order;
- remain at or below 32 KiB.

Phase changes take effect on the next user message. They do not require Hook reinitialization or a new chat.

## Boundaries

- Phase documents under `.phasering/modes/` may be committed to version control.
- Do not edit `.phasering/runtime/`; it contains local selection and health state.
- Do not edit `.codex/hooks/phasering.ps1` manually. Re-run `PhaseRing: Initialize Project` after moving the project or when diagnostics request repair.
- A default phase's test marker should appear only on the first user-visible message of a turn. It confirms injection; it does not mean the Hook ran repeatedly within that turn.

---

# PhaseRing 项目指南

PhaseRing 让当前项目选择一个开发阶段。所选阶段会从下一条用户消息开始，通过项目级 `UserPromptSubmit` Hook 注入 Codex；同一项目的所有聊天共享当前选择。

## 选择阶段

1. 点击 VS Code 状态栏中的 `PhaseRing`。
2. 选择当前阶段。
3. 在 Codex 中发送下一条用户消息。
4. 状态变为 `Active` 后，表示 PhaseRing 已收到与当前选择匹配的真实 Hook 回执。

首次初始化提供五个可编辑阶段：

- `00-free.md`：不附加额外阶段边界；
- `10-discussion.md`：澄清目标、需求和可选方案；
- `20-planning.md`：把确认的目标整理为施工与验收计划；
- `30-implementation.md`：按确认方案实施并验证；
- `40-verification.md`：检查实现是否符合目标。

这些阶段只是起点，不是固定流程。可以按项目需要修改、重命名、新增或删除可选阶段。

## 信任项目 Hook

Codex 要求非托管 Hook 在运行前经过审查和信任。只信任命令明确指向当前项目 `.codex/hooks/phasering.ps1` 的定义。

如果信任一次后仍未出现 `Active`，可以使用我们在 Windows 测试机上观察到的临时方案：

1. 在 VS Code 的 Codex Hook 审查界面信任 PhaseRing 项目 Hook。
2. 在当前项目启动 Codex CLI，运行 `/hooks`，再次信任同一个 Hook。
3. 如果列表中同时出现 `C:\...` 和 `c:\...` 等两种路径写法，确认两条都已信任。
4. 在 VS Code 中运行 `Developer: Reload Window`，然后再发送一条消息。

两条信任记录只是单机观察，不是官方 Hook 要求。如果一次信任已经能够得到 `Active`，无需制造重复记录。

## 自定义阶段

在 `.phasering/modes/` 顶层添加 Markdown 文件。每个文件必须：

- 使用 UTF-8 编码；
- 第一个有效内容是 Markdown 一级标题；
- 保持第一个正文段落简短，因为它会显示在阶段列表中；
- 使用 `10-`、`20-` 等数字前缀控制显示顺序；
- 不超过 32 KiB。

阶段修改从下一条用户消息开始生效，不需要重新初始化 Hook，也不需要新建聊天。

## 使用边界

- `.phasering/modes/` 中的阶段文档可以提交到版本控制。
- 不要手动修改 `.phasering/runtime/`，它保存本地选择和健康状态。
- 不要手动修改 `.codex/hooks/phasering.ps1`。项目移动或诊断要求修复时，请重新运行 `PhaseRing: Initialize Project`。
- 默认阶段中的测试标记只应出现在一轮中第一条对用户可见的消息上。它用于确认注入，不代表 Hook 在同一轮反复运行。
