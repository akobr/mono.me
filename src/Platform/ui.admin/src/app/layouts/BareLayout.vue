<template>
  <div class="min-h-dvh flex flex-col items-center justify-center bg-(--ui-bg-muted) px-4 py-10">
    <div class="mb-6 flex items-center gap-2 text-lg font-semibold">
      <img src="/favicon.svg" alt="" class="size-7" />
      <span>2S Admin</span>
    </div>
    <div class="w-full max-w-md">
      <slot />
    </div>
  </div>
</template>
