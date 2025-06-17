import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import clsx from 'clsx';
import styles from './index.module.css';

const features = [
  {
    title: 'Home Hub',
    svg: 'https://cdn.jsdelivr.net/npm/@fluentui/svg-icons/icons/home_24_filled.svg',
    description: 'All your MyTel‑U apps in one place.',
  },
  {
    title: 'Attendance',
    svg: 'https://cdn.jsdelivr.net/npm/@fluentui/svg-icons/icons/calendar_checkmark_24_filled.svg',
    description: 'Track your semester attendance with ease.',
  },
  {
    title: 'Schedule',
    svg: 'https://cdn.jsdelivr.net/npm/@fluentui/svg-icons/icons/calendar_clock_24_filled.svg',
    description: 'View and export your course timetable.',
  },
  {
    title: 'Community Tools',
    svg: 'https://cdn.jsdelivr.net/npm/@fluentui/svg-icons/icons/apps_24_filled.svg',
    description: 'Explore extra tools developed by the community.',
  },
];

function Feature({ svg, title, description }) {
  return (
    <div className={clsx('col col--6')}>
      <div className={clsx('card', styles.featureCard)}>
        <img src={svg} alt={title} className={styles.featureIcon} />
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function Home() {
  return (
    <Layout
      title="MyTel‑U WinUI3 Application"
      description="Desktop app by the community for MyTel‑U on Windows 10/11"
    >
      <header className={styles.hero}>
        <div className={clsx('container', styles.heroInner)}>
          <div className={clsx('row', styles.heroContent)}>
            {/* Left Content */}
            <div className={clsx('col col--6')}>
              <h1 className={styles.heroTitle}>MyTel‑U Launcher</h1>
              <p className={styles.heroSubtitle}>
                A community‑built WinUI3 desktop Application for MyTel‑U, featuring attendance, schedules, and more.
              </p>
              <div className={styles.heroCta}>
                <Link className="button button--primary button--lg" to="https://github.com/GID0317/MyTel-U_WinUI3/releases/latest">
                  Download Latest Version
                </Link>
                <Link className="button button--secondary button--lg" to="https://github.com/GID0317/MyTel-U_WinUI3">
                  View on GitHub
                </Link>
              </div>
            </div>

            {/* Right Image */}
            <div className={clsx('col col--6', styles.heroImageContainer)}>
              <img src="img/MyTel-UHome.png" alt="App Preview" className={styles.heroImage} />
            </div>
          </div>
        </div>
      </header>

      <main>
        <section className={clsx('container', styles.features)}>
          <div className="row">
            {features.map((props, idx) => (
              <Feature key={idx} {...props} />
            ))}
          </div>
        </section>

        <section className={clsx('container', styles.screenshotSection)}>
          <h2>UI Overview</h2>
          <img
            src="img/UIOverviewBanner.webp"
            alt="App Preview"
            className={styles.screenshotImg}
          />
        </section>
      </main>
    </Layout>
  );
}
