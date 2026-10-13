import { z } from 'zod'
import { isValidKeyValueKey } from '@42for.net/2splatform.sdk'
import { maxValueBytes, utf8Length } from '@/utils/key-values'

// The create and edit form of a pair (key-values.md): the key rules of the API, the level, the value size.
export const keyValueKeySchema = z
  .string()
  .trim()
  .min(1, 'Enter a key.')
  .refine(isValidKeyValueKey, 'Use dot-separated segments of letters, digits and underscores (not starting with a digit), at most 128 characters.')

export const keyValueFormSchema = z.object({
  key: keyValueKeySchema,
  protection: z.enum(['Public', 'Standard', 'Hsm', 'Byok', 'Hyok']),
  value: z.string().refine((value) => utf8Length(value) <= maxValueBytes, `The value is longer than ${maxValueBytes} bytes.`),
  contentType: z.string().trim().max(128).optional(),
})

export type KeyValueForm = z.infer<typeof keyValueFormSchema>
