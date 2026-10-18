type ContentBase = { id: string; title: string | null; createdAtUtc: string; updatedAtUtc: string }
export type ContentSummary = ContentBase & ({ type: 'note' } | { type: 'link'; url: string })
export type ContentDetail = ContentBase & ({ type: 'note'; body: string } | { type: 'link'; url: string })
export type ContentInput = { title: string | null } & ({ type: 'note'; body: string } | { type: 'link'; url: string })
export type ContentPatch = { title?: string; clearTitle?: true; body?: string; url?: string }
