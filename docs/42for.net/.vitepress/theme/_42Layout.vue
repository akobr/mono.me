<script setup>
import DefaultTheme from 'vitepress/theme'

const { Layout } = DefaultTheme
</script>

<template>
  <Layout>
    <template #home-features-after>
        <section id="special-sponsor">
            <span class="lead">Proudly powering</span>
            <a href="https://huddlestock.com/" target="_blank">
                <img
                height="42"
                src="/huddlestock_white.svg"
                alt="huddlestock"
                />
            </a>
            <span class="lead">Cloud-based and scalable finance platform</span>
        </section>
    </template>
  </Layout>
</template>