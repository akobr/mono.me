import type { AnnotationTypeCode } from '@/utils/annotation-key'

// Type schemas and templates of the mock API. The execution schema exercises every renderer of the schema form.

const executionSchema = {
  $schema: 'http://json-schema.org/draft-07/schema#',
  type: 'object',
  properties: {
    enabled: { type: 'boolean', title: 'Enabled', description: 'Turns the execution on.' },
    retries: { type: 'integer', minimum: 0, maximum: 10, title: 'Retries', description: 'Attempts after a failure.' },
    timeout: { type: 'number', minimum: 0, multipleOf: 0.5, description: 'Seconds before an attempt is abandoned.' },
    endpoint: {
      type: 'string',
      format: 'uri',
      description: 'Usually a binding, for example `@config("/baseUrl")`: the base URL the context configures.',
    },
    mode: { type: 'string', enum: ['batch', 'stream', 'manual'] },
    region: {
      type: 'string',
      oneOf: [
        { const: 'eu-west', title: 'Europe (Ireland)' },
        { const: 'us-east', title: 'US East (Virginia)' },
        { const: 'ap-south', title: 'Asia Pacific (Mumbai)' },
      ],
    },
    apiKey: { type: 'string', format: 'password', description: 'Prefer a binding to a secret.' },
    notes: { type: 'string', 'x-multiline': true },
    startDate: { type: 'string', format: 'date' },
    window: { type: 'string', format: 'time', title: 'Maintenance window' },
    notify: { type: 'array', items: { type: 'string', format: 'email' }, title: 'Notify' },
    channels: { type: 'array', items: { type: 'string', enum: ['email', 'sms', 'push'] } },
    tiers: {
      type: 'array',
      items: {
        type: 'object',
        properties: { name: { type: 'string' }, limit: { type: 'integer', minimum: 0 } },
        required: ['name'],
      },
    },
    headers: { type: 'object', additionalProperties: { type: 'string' }, description: 'Extra HTTP headers.' },
    schedule: {
      type: 'object',
      title: 'Schedule',
      properties: {
        cron: { type: 'string', pattern: '^\\S+ \\S+ \\S+ \\S+ \\S+$' },
        timeZone: { type: 'string' },
      },
    },
    delivery: {
      oneOf: [
        { title: 'Email', type: 'object', properties: { to: { type: 'string', format: 'email' } }, required: ['to'] },
        { title: 'Webhook', type: 'object', properties: { url: { type: 'string', format: 'uri' } }, required: ['url'] },
      ],
    },
    advanced: { type: 'object', patternProperties: { '^x-': {} }, description: 'Vendor extensions (x-…).' },
  },
  required: ['retries'],
}

const contextSchema = {
  type: 'object',
  properties: {
    baseUrl: { type: 'string', format: 'uri' },
    region: { type: 'string', enum: ['eu-west', 'us-east', 'ap-south'] },
    maintenance: { type: 'boolean' },
  },
}

export const typeSchemas: Partial<Record<AnnotationTypeCode, object>> = {
  exe: executionSchema,
  uxe: executionSchema,
  cnt: contextSchema,
}

export const templates: Partial<Record<AnnotationTypeCode, Record<string, unknown>>> = {
  exe: { enabled: true, retries: 3, timeout: 30, mode: 'batch', channels: ['email'] },
  uxe: { enabled: true, retries: 1, timeout: 10, mode: 'stream' },
}
