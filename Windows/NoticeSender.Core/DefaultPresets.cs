namespace NoticeSender.Core;
public static class DefaultPresets
{
    public static List<MessagePreset> Create() => [new MessagePreset
    {
        Kind = "직접입력",
        Name = "직접입력",
        PresentTemplate = "{{notice}}",
        VideoTemplate = "{{notice}}",
        AbsentTemplate = "{{notice}}"
    }, new MessagePreset
    {
        Kind = "첫 수업",
        Name = "첫 수업 기본",
        PresentTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : 첫 수업이므로 과제 점수 없습니다.\n수업 태도 : {{attitude_stars}}\n테스트 : {{exam_unit}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n첫 수업으로 배부된 과제가 없어 숙제 현황 없습니다.\n다음 수업부터 숙제 현황 안내드릴 수 있도록 하겠습니다.\n\n④ 클리닉 테스트\n\n{{exam_unit}}\n\n<개별 코멘트>\n\n- {{attitude_comment}}\n{{homework_comment_line}}{{test_comment_line}}\n<공통 코멘트>\n\n- {{notice}}",
        VideoTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : 첫 수업이므로 과제 점수 없습니다.\n수업 태도 : 동영상 시청으로 점수 없습니다.\n테스트 : {{exam_unit}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n첫 수업으로 숙제 현황 안내가 없습니다.\n\n<개별 코멘트>\n\n영상 시청 후 수업 내용을 토대로 과제를 풀어주세요.\n\n<공통 코멘트>\n\n- {{notice}}",
        AbsentTemplate = "{{nickname}}의 [{{date}}] 첫 수업 결석 안내입니다.\n\n{{notice}}"
    }, new MessagePreset
    {
        Kind = "일반 수업",
        Name = "숙제와 시험 모두 있는 수업",
        PresentTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : {{homework_stars}}\n수업 태도 : {{attitude_stars}}\n테스트 : {{test_stars}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n지난 수업 총 {{homework_max}} 문제의 과제가 있었습니다.\n숙제 성취도 : {{homework_percent}}\n\n④ 클리닉 테스트\n\n{{exam_unit}}에 대해 테스트를 진행했습니다.\n학생 점수 : {{test_percent}}\n\n<개별 코멘트>\n\n- {{attitude_comment}}\n{{homework_comment_line}}{{test_comment_line}}\n<공통 코멘트>\n\n- {{notice}}",
        VideoTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n동영상 시청으로 과제 및 수업 태도 점수는 없습니다.\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n영상 시청 후 수업 내용을 토대로 과제를 풀어주세요.\n\n<공통 코멘트>\n\n- {{notice}}",
        AbsentTemplate = "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n\n{{notice}}"
    }, new MessagePreset
    {
        Kind = "시험만",
        Name = "시험만 있는 수업",
        PresentTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : 오늘은 진도상 숙제검사를 생략했습니다.\n수업 태도 : {{attitude_stars}}\n테스트 : {{test_stars}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n오늘은 진도상 숙제검사를 생략했습니다.\n\n④ 클리닉 테스트\n\n{{exam_unit}}에 대해 테스트를 진행했습니다.\n학생 점수 : {{test_percent}}\n\n<개별 코멘트>\n\n- {{attitude_comment}}\n{{test_comment_line}}\n<공통 코멘트>\n\n- {{notice}}",
        VideoTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : 오늘은 진도상 숙제검사를 생략했습니다.\n수업 태도 : 동영상 시청으로 점수 없습니다.\n테스트 : {{test_stars}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n오늘은 진도상 숙제검사를 생략했습니다.\n\n④ 클리닉 테스트\n\n{{exam_unit}}\n\n<공통 코멘트>\n\n- {{notice}}",
        AbsentTemplate = "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n시험범위: {{exam_unit}}\n\n{{notice}}"
    }, new MessagePreset
    {
        Kind = "숙제만",
        Name = "숙제만 있는 수업",
        PresentTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : {{homework_stars}}\n수업 태도 : {{attitude_stars}}\n테스트 : {{exam_unit}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n지난 수업 총 {{homework_max}} 문제의 과제가 있었습니다.\n숙제 성취도 : {{homework_percent}}\n\n④ 시험범위\n\n{{exam_unit}}\n\n<개별 코멘트>\n\n- {{attitude_comment}}\n{{homework_comment_line}}\n<공통 코멘트>\n\n- {{notice}}",
        VideoTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 수업 안내드리겠습니다.\n\n과제 점수 : 동영상 시청으로 점수 없습니다.\n수업 태도 : 동영상 시청으로 점수 없습니다.\n테스트 : {{exam_unit}}\n\n① 진도 및 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n③ 숙제 현황\n\n지난 숙제가 완료되었는지 확인해주시기 바랍니다.\n\n④ 시험범위\n\n{{exam_unit}}\n\n<공통 코멘트>\n\n- {{notice}}",
        AbsentTemplate = "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n시험범위: {{exam_unit}}\n\n{{notice}}"
    }, new MessagePreset
    {
        Kind = "모의고사",
        Name = "모의고사 기본",
        PresentTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 모의고사 결과 안내드리겠습니다.\n\n① 수업 내용\n\n{{progress}}\n\n② 과제\n\n{{assignment}}\n\n{{exam_sections}}\n<공통 코멘트>\n\n- {{notice}}",
        VideoTemplate = "안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.\n{{nickname}}의 [{{date}}] 모의고사 자료 안내드리겠습니다.\n\n{{exam_summary_sections}}\n자료를 수령해서 풀고 틀린 것을 공부해주세요.\n\n<공통 코멘트>\n\n- {{notice}}",
        AbsentTemplate = "{{nickname}}의 [{{date}}] 모의고사 결석 안내입니다.\n\n{{exam_summary_sections}}\n{{notice}}",
        MockExamCount = 3
    }, ];
}
