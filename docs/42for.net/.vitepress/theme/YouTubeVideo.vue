<script setup>
import { ref } from 'vue'

// A YouTube video that loads nothing from YouTube until the visitor clicks it: no cookies, no storage and no request
// to Google before then. The placeholder is drawn locally (no thumbnail from i.ytimg.com); the click inserts the
// privacy-enhanced player (youtube-nocookie.com), which then plays at once.
const props = defineProps({
  id: { type: String, required: true },
  title: { type: String, required: true },
})

const playing = ref(false)
</script>

<template>
  <div class="yt-video">
    <iframe
      v-if="playing"
      :src="`https://www.youtube-nocookie.com/embed/${props.id}?autoplay=1`"
      :title="props.title"
      frameborder="0"
      allow="autoplay; encrypted-media; picture-in-picture; web-share"
      allowfullscreen
    ></iframe>
    <button v-else type="button" class="yt-placeholder" @click="playing = true">
      <span class="yt-play" aria-hidden="true"></span>
      <span class="yt-title">{{ props.title }}</span>
      <span class="yt-note">Plays from YouTube (Google); YouTube's cookies and privacy policy apply once you start it.</span>
    </button>
  </div>
</template>

<style scoped>
.yt-video {
  width: 100%;
  max-width: 560px;
  aspect-ratio: 16 / 9;
  margin: 16px auto;
}
.yt-video iframe,
.yt-placeholder {
  width: 100%;
  height: 100%;
  border-radius: 8px;
}
.yt-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 16px;
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--vp-c-divider);
  cursor: pointer;
  text-align: center;
}
.yt-placeholder:hover .yt-play,
.yt-placeholder:focus-visible .yt-play {
  background: #ff0000;
}
.yt-play {
  width: 68px;
  height: 48px;
  border-radius: 12px;
  background: #212121;
  position: relative;
  transition: background 0.2s;
}
.yt-play::after {
  content: '';
  position: absolute;
  left: 27px;
  top: 14px;
  border-style: solid;
  border-width: 10px 0 10px 17px;
  border-color: transparent transparent transparent #fff;
}
.yt-title {
  font-weight: 600;
}
.yt-note {
  font-size: 12px;
  color: var(--vp-c-text-2);
}
</style>
