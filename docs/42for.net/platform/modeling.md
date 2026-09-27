# How to model a product

The annotation types are fixed. The work is deciding what each one means for your product, and sticking to that decision. A good model is one you can point at during an incident and trust.

Start from the questions you actually ask. Then give each question an annotation that answers it.

| Question | Annotation that answers it |
| --- | --- |
| What are the distinct parties, versions, markets, or product models I operate? | Subject |
| In which worlds does each of them run? | Context |
| Which capabilities do I ship, configure, or watch? | Responsibility |
| Which of those capabilities does this subject consume, on what commercial terms? | Usage |
| What is the setup of that capability in one world? | Execution |
| Which job, event, or pipeline inside a capability has its own life? | Unit, and a unit of execution per world |

If a question you care about has no row, the grain is wrong: a subject is too coarse, or a responsibility is hiding two capabilities, or a context is being used for something that is really another subject.

## A sequence that works

1. **Name the subject** as the thing you want a page for. "Show me Northwind" should open one subject, with its usages and contexts underneath. If you would naturally say "show me the production cluster of Northwind" as often as you say "show me Northwind", production is a context and Northwind is still the subject.
2. **Name the contexts** as the axis that changes the setup while the subject stays the same. Environment, legal entity, release still in production, trim, cell, jurisdiction. One context is enough when there is only one world; give it a dull name such as `production`.
3. **Name the responsibilities** at the grain you deploy, sell, or debug. A modulith satellite, a service, a module of a monolith, a subsystem of a device. Prefer a name a teammate already uses.
4. **Let usages appear where a subject really consumes a responsibility.** Put the plan, the package, and the entitlement there. A responsibility the subject cannot access has no usage.
5. **Let executions appear where that usage runs in a context.** Put only the override there. If the execution document grows a copy of the usage document, the value belongs one level up.
6. **Add a unit** when a responsibility contains a named job or trigger. The unit definition is the default trigger. The unit of execution carries a per-setup override.

Creating the deepest record is enough to start. Storyteller creates a missing subject, context, responsibility, usage, execution, or unit and links the names on the parents. You can come back and set titles, descriptions, and configuration on the parents afterwards.

## Where a value belongs

Write each value on the most general annotation for which it is always true.

| The value is true for | Write it on |
| --- | --- |
| Every customer of this capability | The responsibility |
| This subject, whichever capability you look at | The subject |
| This subject consuming this capability, in every context | The usage |
| This subject in this world, whichever capability you look at | The context |
| This capability for this subject in this world | The execution |
| This job for this subject in this world | The unit of execution |

A feature flag that is off for one sandbox belongs on that execution. A data-residency region that applies to every capability of the customer belongs on the subject or the context, depending on whether the region follows the customer or the environment. A plan belongs on the usage.

Arrays are unioned along the merge, which suits allow-lists and feature packages: the responsibility offers `vat`, the usage adds `e-invoice`, the execution adds nothing, and the effective list contains both. A scalar such as `retries` or `currency` is a choice, so the most specific document that sets it wins.

## Schemas follow the owner

Put a descendant-type schema on the annotation that has the right to define the shape.

- A responsibility defines what a valid **usage** and a valid **unit** of itself must contain. Every customer of invoicing must have a `plan`.
- A subject defines what a valid **context** of itself must contain. Every environment of this customer must name a `region`.
- An execution schema is for the rare case where the pair, or the context, adds a requirement of its own.

The schema checks the stored document of the annotation you write. A required `plan` on a usage has to be present on that usage, even if some ancestor also mentions a plan.

## Names

Use short, stable, lowercase names. They become segments of every child key, and they show up in logs and support tickets. Rename is a new key. Prefer `invoicing` over `invoice-service-v3`, and keep the version in a label, a context, or the configuration when it is a real axis of the product.

## A smaller picture, on purpose

Some products never need units. Some never need more than one context. Model the axes you operate, and leave the rest unused. The relations you do create stay readable, because the key of an execution always spells the subject, the responsibility, and the context.

The [examples](examples/) are this sequence applied to products that look different and use the same seven types. [Using the platform](using-the-platform) shows how those records are addressed and read.
