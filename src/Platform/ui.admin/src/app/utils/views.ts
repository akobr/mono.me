/** The view every project has (`Constants.DefaultViewName`); the API sets `View.IsDefault` from this name. */
export const defaultViewName = 'default'

/** Whether a view is the default one, the main (production) view of its project. Case-sensitive, like the API. */
export function isDefaultView(view: string | null | undefined): boolean {
  return view === defaultViewName
}

/** The badge after the name of the default view in the sidebar and the switcher (424-43). */
export const defaultViewBadge = { label: 'prod', color: 'primary', variant: 'subtle', size: 'sm' } as const
