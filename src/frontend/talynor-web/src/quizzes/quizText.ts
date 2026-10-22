// .NET Char.IsWhiteSpace / String.Trim includes U+0085 and excludes U+FEFF,
// unlike JavaScript trim(). Preserve all question/option text except for comparison.
export const trimQuizWhitespace = (value: string) => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '')
