// The people of the mock data. Ada is the signed-in user of every dataset; Grace and Alan are also members of the
// previous standard dataset (db.ts). The others appear in the showcase projects only.

export const people = {
  ada: { id: 'user_01MOCKADA', name: 'Ada Lovelace', email: 'ada@example.com' },
  grace: { id: 'user_01MOCKGRACE', name: 'Grace Hopper', email: 'grace@example.com' },
  alan: { id: 'user_01MOCKALAN', name: 'Alan Turing', email: 'alan@example.com' },
  margaret: { id: 'user_01MOCKMARGARET', name: 'Margaret Hamilton', email: 'margaret@example.com' },
  barbara: { id: 'user_01MOCKBARBARA', name: 'Barbara Liskov', email: 'barbara@example.com' },
  edsger: { id: 'user_01MOCKEDSGER', name: 'Edsger Dijkstra', email: 'edsger@example.com' },
  radia: { id: 'user_01MOCKRADIA', name: 'Radia Perlman', email: 'radia@example.com' },
  donald: { id: 'user_01MOCKDONALD', name: 'Donald Knuth', email: 'donald@example.com' },
  frances: { id: 'user_01MOCKFRANCES', name: 'Frances Allen', email: 'frances@example.com' },
  ken: { id: 'user_01MOCKKEN', name: 'Ken Thompson', email: 'ken@example.com' },
  hedy: { id: 'user_01MOCKHEDY', name: 'Hedy Lamarr', email: 'hedy@example.com' },
  annie: { id: 'user_01MOCKANNIE', name: 'Annie Easley', email: 'annie@example.com' },
} as const

export type PersonaId = keyof typeof people

/** Names and e-mails by account id, for mockDirectory. */
export const peopleDirectory: Record<string, { name: string; email: string }> = Object.fromEntries(
  Object.values(people).map((person) => [person.id, { name: person.name, email: person.email }]),
)
