import React from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';

export default function NotFound() {
  return (
    <Layout title="Page Not Found">
      <div
        style={{
          padding: '5rem 1rem',
          textAlign: 'center',
        }}>
        <h1 style={{ fontSize: '3rem', marginBottom: '1rem' }}>404</h1>
        <p style={{ fontSize: '1.25rem', marginBottom: '2rem' }}>
          Oops! The page you're looking for doesn't exist.
        </p>
        <Link className="button button--primary button--lg" to="/">
          Back to Home
        </Link>
      </div>
    </Layout>
  );
}
