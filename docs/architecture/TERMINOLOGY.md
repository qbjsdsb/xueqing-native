# Product Terminology Contract

Chinese-first product language must be consistent while each platform uses its native localization resources.

Core terms:

- Learning Case → 学情问题 / 问题 (context dependent; avoid showing engineering word `Case` to teachers);
- Evidence → 证据 / 观察记录;
- Intervention → 干预 / 教学处理;
- Assessment / Verification → 评估 / 验证;
- Next Action → 下一步;
- pending_verification → 待验证;
- stable → 已稳定;
- closed → 已关闭;
- Responsible Teacher / Lead → 教学主责;
- Personal Workspace → 我的教学;
- Organization Workspace → 机构工作区;
- Quick Capture → 记录问题 / 快速记录 depending on surface.

Do not hard-code user-facing strings in domain/ViewModel logic. Windows and Android may phrase platform chrome differently but must preserve domain meaning.
